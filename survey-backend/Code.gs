/**
 * ZugDesk survey backend (Google Apps Script web app).
 *
 * Contract with survey/index.html (do not change without changing the page):
 *   POST  text/plain JSON {surveyVersion, submissionId, language, answers}
 *         -> {"ok":true} only after the row is written (or was already written
 *            for the same submissionId), otherwise {"ok":false,"error":"..."}.
 *   GET   ?zugpad_status=<id>&zugpad_callback=<fn>
 *         -> fn({"saved":true|false});   (read-only confirmation lookup)
 * The "zugpad_" parameter names are kept from the original ZugPad release so
 * that pages already open in people's browsers keep working.
 *
 * Each field is written to the column whose row-1 header matches one of its
 * names in HEADERS (case, spaces and punctuation ignored). Only a field with no
 * matching header gets a new column, added at the right end of row 1. Existing
 * columns and older responses are never moved, renamed or overwritten.
 * Run checkColumns() before deploying to see the mapping without writing.
 */

// Paste the same Google Sheets URL that the previous working Code.gs used.
var SPREADSHEET_URL = 'PASTE_YOUR_GOOGLE_SHEET_URL_HERE';
var SHEET_NAME = 'Responses';

// Accepted header names per field; the first one is used if a column must be added.
var HEADERS = {
  timestamp:      ['Timestamp'],
  submissionId:   ['Submission ID', 'submissionId'],
  surveyVersion:  ['Survey version', 'surveyVersion'],
  language:       ['Language'],
  tools:          ['Current tools', 'tools'],
  tools_other:    ['Current tools (other)', 'Current tools other', 'Other tools', 'tools_other'],
  incident:       ['Recent example', 'incident'],
  friction:       ['Difficult tasks', 'friction'],
  friction_other: ['Difficult tasks (other)', 'Difficult tasks other', 'Other difficult tasks', 'friction_other'],
  workload:       ['Recent workload (v2)', 'workload'],
  device_current: ['Current device used (v2)', 'device_current'],
  barrier:        ['Barriers', 'barrier'],
  barrier_other:  ['Barriers (other)', 'Barriers other', 'Other barriers', 'barrier_other'],
  feature:        ['Most useful feature', 'feature'],
  feature_other:  ['Most useful feature (other)', 'feature_other'],
  device:         ['Device', 'device']
};

var LANGUAGES = ['en', 'he', 'yi', 'ru', 'fr', 'es'];

// Must match the option IDs in survey/index.html exactly.
var QUESTIONS = {
  v2: {
    tools:          { type: 'checks', options: ['wa', 'email', 'paper', 'sheet', 'notes', 'app', 'memory', 'other'] },
    tools_other:    { type: 'text', max: 250 },
    incident:       { type: 'text', max: 500 },
    friction:       { type: 'checks', limit: 2, options: ['intake', 'find', 'candidates', 'duplicates', 'status', 'followup', 'sharing', 'none', 'other'] },
    friction_other: { type: 'text', max: 250 },
    workload:       { type: 'radio', options: ['none', '1_5', '6_20', '21_50', 'over_50', 'unsure'] },
    device_current: { type: 'radio', options: ['phone', 'desktop', 'both', 'neither', 'unsure'] },
    barrier:        { type: 'checks', limit: 2, options: ['double', 'learn', 'move', 'privacy', 'filter', 'enough', 'none', 'other'] },
    barrier_other:  { type: 'text', max: 250 }
  },
  // First release (no surveyVersion in the payload), kept for pages cached in browsers.
  v1: {
    tools:          { type: 'checks', options: ['wa', 'email', 'paper', 'sheet', 'app', 'memory', 'other'] },
    tools_other:    { type: 'text', max: 250 },
    friction:       { type: 'checks', limit: 2, options: ['intake', 'find', 'updates', 'duplicates', 'status', 'followup', 'sharing', 'none', 'other'] },
    friction_other: { type: 'text', max: 250 },
    incident:       { type: 'text', max: 500 },
    feature:        { type: 'radio', options: ['capture', 'search', 'status', 'reminder', 'duplicate', 'share', 'none', 'other'] },
    feature_other:  { type: 'text', max: 250 },
    device:         { type: 'radio', options: ['phone', 'desktop', 'both', 'neither'] },
    barrier:        { type: 'checks', limit: 2, options: ['double', 'privacy', 'learn', 'enough', 'noapp', 'other'] },
    barrier_other:  { type: 'text', max: 250 }
  }
};

function doPost(e) {
  var result;
  try {
    var body = e && e.postData ? String(e.postData.contents || '') : '';
    if (!body || body.length > 20000) throw new SurveyError_('bad_request');
    var payload;
    try { payload = JSON.parse(body); } catch (err) { throw new SurveyError_('bad_json'); }
    result = saveSubmission_(payload);
  } catch (err) {
    console.error('doPost failed: ' + (err && (err.stack || err.message) || err));
    result = { ok: false, error: err instanceof SurveyError_ ? err.message : 'server_error' };
  }
  return json_(result);
}

function doGet(e) {
  var p = (e && e.parameter) || {};
  if (p.zugpad_status === undefined) return json_({ ok: true, service: 'ZugDesk survey' });
  var saved = false;
  try {
    var id = String(p.zugpad_status);
    if (validId_(id)) saved = isSaved_(openSheet_(), id);
  } catch (err) {
    console.error('status lookup failed: ' + (err && err.stack || err));
  }
  var out = JSON.stringify({ saved: saved });
  var callback = String(p.zugpad_callback || '');
  if (/^[A-Za-z_$][\w$]{0,99}$/.test(callback)) {
    return ContentService.createTextOutput(callback + '(' + out + ');')
      .setMimeType(ContentService.MimeType.JAVASCRIPT);
  }
  return ContentService.createTextOutput(out).setMimeType(ContentService.MimeType.JSON);
}

function saveSubmission_(payload) {
  if (!payload || typeof payload !== 'object') throw new SurveyError_('bad_request');
  var id = String(payload.submissionId || '');
  if (!validId_(id)) throw new SurveyError_('bad_submission_id');
  var version = payload.surveyVersion === undefined ? 'v1' : String(payload.surveyVersion);
  var questions = QUESTIONS[version];
  if (!questions) throw new SurveyError_('unknown_survey_version');
  var language = LANGUAGES.indexOf(payload.language) >= 0 ? payload.language : '';
  var values = cleanAnswers_(payload.answers, questions);

  var keys = ['timestamp', 'submissionId', 'surveyVersion', 'language'].concat(Object.keys(questions));
  var lock = LockService.getScriptLock();
  if (!lock.tryLock(25000)) throw new SurveyError_('busy');
  try {
    var sheet = openSheet_();
    var columns = ensureColumns_(sheet, keys);
    if (isSaved_(sheet, id, columns)) return { ok: true, duplicate: true };
    var row = [];
    for (var i = 0; i < columns.width; i++) row.push('');
    row[columns.field.timestamp] = new Date();
    row[columns.field.submissionId] = id;
    row[columns.field.surveyVersion] = version;
    row[columns.field.language] = language;
    for (var key in values) row[columns.field[key]] = values[key];
    sheet.appendRow(row);
    SpreadsheetApp.flush();
  } finally {
    lock.releaseLock();
  }
  return { ok: true };
}

function cleanAnswers_(answers, questions) {
  if (!answers || typeof answers !== 'object' || Array.isArray(answers)) throw new SurveyError_('bad_answers');
  var values = {}, answered = false;
  for (var key in answers) {
    var q = questions[key], value = answers[key];
    if (!q) throw new SurveyError_('unknown_question:' + key);
    if (q.type === 'text') {
      if (typeof value !== 'string') throw new SurveyError_('bad_value:' + key);
      value = safeText_(value.trim().slice(0, q.max));
    } else if (q.type === 'radio') {
      if (typeof value !== 'string' || (value && q.options.indexOf(value) < 0)) throw new SurveyError_('bad_value:' + key);
    } else {
      if (!Array.isArray(value)) throw new SurveyError_('bad_value:' + key);
      var picked = [];
      for (var i = 0; i < value.length; i++) {
        if (q.options.indexOf(value[i]) < 0) throw new SurveyError_('bad_value:' + key);
        if (picked.indexOf(value[i]) < 0) picked.push(value[i]);
      }
      if (q.limit && picked.length > q.limit) throw new SurveyError_('too_many:' + key);
      value = picked.join(', ');
    }
    if (value) { values[key] = value; answered = true; }
  }
  if (!answered) throw new SurveyError_('empty');
  return values;
}

// Text starting with = + - @ would be read by Sheets as a formula.
function safeText_(s) {
  return /^[=+\-@]/.test(s) ? "'" + s : s;
}

function validId_(id) {
  return /^[A-Za-z0-9_-]{6,64}$/.test(id);
}

function openSheet_() {
  if (!/^https:\/\/docs\.google\.com\/spreadsheets\//.test(SPREADSHEET_URL)) throw new Error('SPREADSHEET_URL is not set');
  var book = SpreadsheetApp.openByUrl(SPREADSHEET_URL);
  var sheet = book.getSheetByName(SHEET_NAME);
  if (!sheet) throw new Error('Sheet tab "' + SHEET_NAME + '" not found');
  return sheet;
}

function norm_(s) {
  return String(s).toLowerCase().replace(/[^a-z0-9]/g, '');
}

// Finds each field's column by its HEADERS names. Fields without a column are left out of columns.field.
function readColumns_(sheet) {
  var width = sheet.getLastColumn(), byHeader = {}, field = {};
  var headers = width ? sheet.getRange(1, 1, 1, width).getDisplayValues()[0] : [];
  for (var i = 0; i < headers.length; i++) {
    var n = norm_(headers[i]);
    if (n && !(n in byHeader)) byHeader[n] = i;
  }
  for (var key in HEADERS) {
    for (var j = 0; j < HEADERS[key].length; j++) {
      if (norm_(HEADERS[key][j]) in byHeader) { field[key] = byHeader[norm_(HEADERS[key][j])]; break; }
    }
  }
  return { field: field, headers: headers, width: width };
}

function ensureColumns_(sheet, keys) {
  var columns = readColumns_(sheet), missing = [];
  for (var i = 0; i < keys.length; i++) if (!(keys[i] in columns.field)) missing.push(keys[i]);
  if (missing.length) {
    sheet.getRange(1, columns.width + 1, 1, missing.length)
      .setValues([missing.map(function (k) { return HEADERS[k][0]; })]);
    for (var j = 0; j < missing.length; j++) columns.field[missing[j]] = columns.width + j;
    columns.width += missing.length;
  }
  return columns;
}

function isSaved_(sheet, id, columns) {
  columns = columns || readColumns_(sheet);
  var col = columns.field.submissionId, last = sheet.getLastRow();
  if (col === undefined || last < 2) return false;
  return !!sheet.getRange(2, col + 1, last - 1, 1).createTextFinder(id).matchEntireCell(true).findNext();
}

function json_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}

function SurveyError_(message) { this.message = message; }

/**
 * Read-only. Run from the Apps Script editor (select "checkColumns", then Run)
 * before deploying. Logs which existing column each field will use; a field
 * marked NEW would get a new column. Nothing is written.
 */
function checkColumns() {
  var columns = readColumns_(openSheet_()), lines = [];
  var keys = ['timestamp', 'submissionId', 'surveyVersion', 'language'].concat(Object.keys(QUESTIONS.v2));
  for (var i = 0; i < keys.length; i++) {
    var col = columns.field[keys[i]];
    lines.push(keys[i] + ' -> ' + (col === undefined ? 'NEW column "' + HEADERS[keys[i]][0] + '"'
      : 'column ' + (col + 1) + ' "' + columns.headers[col] + '"'));
  }
  console.log(lines.join('\n'));
  return lines;
}

/**
 * Run from the Apps Script editor (select "testSave", then Run) to check that
 * this code can write to the Responses tab. It adds one row whose Submission ID
 * starts with "selftest"; delete that row afterwards.
 */
function testSave() {
  var id = 'selftest-' + Date.now();
  var result = saveSubmission_({ surveyVersion: 'v2', submissionId: id, language: 'en',
    answers: { tools: ['notes', 'other'], tools_other: 'self test', friction: ['candidates', 'status'],
      workload: '6_20', device_current: 'unsure', barrier: ['none'] } });
  var again = saveSubmission_({ surveyVersion: 'v2', submissionId: id, language: 'en', answers: { workload: 'none' } });
  console.log(JSON.stringify({ first: result, retry: again, savedLookup: isSaved_(openSheet_(), id) }));
}
