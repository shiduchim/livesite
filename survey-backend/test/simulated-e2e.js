// Simulated end-to-end test: the real survey page in Chromium, talking to the
// real Code.gs running in Node against an in-memory "Responses" sheet.
// Google itself is NOT contacted; see survey-backend/README.md for live checks.
// Run: NODE_PATH=$(npm root -g) node survey-backend/test/simulated-e2e.js
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');
const { chromium } = require('playwright');

const ROOT = path.resolve(__dirname, '../..');
const PAGE = 'https://shiduchim.github.io/livesite/survey/';

// ---- Minimal Apps Script services backed by a 2-D array ----------------------
// The 16 Responses headers. Exact names from the owner: Submitted at, Current tools, Difficult tasks,
// Recent example, Recent workload (v2), Current device used (v2), Adoption barriers, Preferred device.
// The other eight are likely names; checkColumns() confirms them on the real Sheet.
const LEGACY_HEADERS = ['Submitted at', 'Submission ID', 'Survey version', 'Language', 'Current tools', 'Current tools (other)',
  'Difficult tasks', 'Difficult tasks (other)', 'Recent example', 'Most useful feature', 'Most useful feature (other)',
  'Preferred device', 'Adoption barriers', 'Barriers (other)', 'Recent workload (v2)', 'Current device used (v2)'];
const LEGACY_ROW = ['2026-10-17 21:00', 'sOLDROW123456', '', 'en', 'WhatsApp or messages, Notebook or paper', '',
  'Tracking offers and responses from both sides', '', 'old answer', 'Find and filter people quickly', '',
  'Mainly on my phone', 'Privacy or confidentiality concerns', '', '', ''];
let grid = [LEGACY_HEADERS.slice(), LEGACY_ROW.slice()];
const width = () => grid.reduce((m, r) => Math.max(m, r.length), 0);
const cell = (r, c) => (grid[r] && grid[r][c] !== undefined ? grid[r][c] : '');
const sheet = {
  getLastColumn: width,
  getLastRow: () => grid.length,
  appendRow(row) { grid.push(row.slice()); },
  getRange(r, c, nr = 1, nc = 1) {
    const rows = () => Array.from({ length: nr }, (_, i) => Array.from({ length: nc }, (_, j) => cell(r - 1 + i, c - 1 + j)));
    return {
      getDisplayValues: () => rows().map(x => x.map(v => (v instanceof Date ? v.toISOString() : String(v)))),
      setValues(v) { v.forEach((row, i) => row.forEach((val, j) => { grid[r - 1 + i] = grid[r - 1 + i] || []; grid[r - 1 + i][c - 1 + j] = val; })); },
      createTextFinder(text) {
        let entire = false;
        const f = { matchEntireCell(b) { entire = b; return f; },
          findNext: () => (rows().some(x => x.some(v => (entire ? String(v) === text : String(v).includes(text)))) ? {} : null) };
        return f;
      }
    };
  }
};
const MimeType = { JSON: 'application/json', JAVASCRIPT: 'application/javascript' };
const gas = vm.createContext({
  console,
  SpreadsheetApp: {
    openByUrl: u => { assert.strictEqual(u, SHEET_URL); return { getSheetByName: n => (n === 'Responses' ? sheet : null) }; },
    flush() {}
  },
  LockService: { getScriptLock: () => ({ tryLock: () => true, releaseLock() {} }) },
  ContentService: { MimeType, createTextOutput: s => ({ content: s, mime: 'text/plain', setMimeType(m) { this.mime = m; return this; } }) }
});
const SHEET_URL = 'https://docs.google.com/spreadsheets/d/TEST_SHEET_ID/edit';
vm.runInContext(fs.readFileSync(path.join(ROOT, 'survey-backend/Code.gs'), 'utf8'), gas);
assert.throws(() => gas.openSheet_(), /SPREADSHEET_URL is not set/);   // placeholder must be replaced
gas.SPREADSHEET_URL = SHEET_URL;

// Rows keyed by field name, using Code.gs's own header mapping.
function rows() {
  const field = JSON.parse(JSON.stringify(gas.readColumns_(sheet).field));
  return grid.slice(1).map(r => Object.fromEntries(Object.entries(field).map(([k, i]) => [k, r[i] === undefined ? '' : r[i]])));
}
const newRows = () => rows().filter(r => r.submissionId !== 'sOLDROW123456');

// ---- Network simulation -------------------------------------------------------
let mode = 'normal';            // normal | down | lostReply
let posts = 0;

async function routeGoogle(route) {
  const req = route.request(), url = new URL(req.url());
  if (mode === 'down') return route.abort('internetdisconnected');
  let out;
  if (req.method() === 'POST') {
    posts++;
    out = gas.doPost({ postData: { contents: req.postData() } });
    if (mode === 'lostReply') return route.abort('connectionreset');   // saved, but the reply never arrives
  } else {
    out = gas.doGet({ parameter: Object.fromEntries(url.searchParams) });
  }
  // Real Apps Script first answers with a 302 to script.googleusercontent.com;
  // Playwright cannot route that second hop, so the final reply is served directly.
  return route.fulfill({ status: 200, contentType: out.mime, headers: { 'Access-Control-Allow-Origin': '*' }, body: out.content });
}

async function routeSite(route) {
  const file = new URL(route.request().url()).pathname.replace('/livesite/', '').replace(/\/$/, '/index.html');
  const full = path.join(ROOT, file);
  if (!fs.existsSync(full)) return route.fulfill({ status: 404, body: 'not found' });
  const type = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.png': 'image/png' }[path.extname(full)];
  return route.fulfill({ status: 200, contentType: type, body: fs.readFileSync(full) });
}

// ---- Helpers -----------------------------------------------------------------
const results = [];
async function test(name, fn) {
  try { await fn(); results.push(['PASS', name]); console.log('PASS', name); }
  catch (e) { results.push(['FAIL', name]); console.log('FAIL', name, '\n   ', e.message); }
}
let browser, context;
async function open(lang) {
  const page = await context.newPage();
  page.on('pageerror', e => console.log('   page error:', e.message));
  await page.goto(PAGE);
  if (lang === 'he') await page.click('#he');
  else if (lang && lang !== 'en') { await page.click('#moreLang'); await page.click(`.lang-choice[data-lang="${lang}"]`); }
  return page;
}
const pick = (page, q, v) => page.check(`input[name="${q}"][value="${v}"]`);
async function submitAndWaitThanks(page) {
  await page.click('button.submit');
  await page.waitForSelector('.thankyou', { timeout: 20000 });
}
async function submitAndWaitError(page) {
  await page.click('button.submit');
  await page.waitForSelector('#message.show', { timeout: 25000 });
  return page.textContent('#message');
}
const lastId = async page => page.evaluate(() => submissionId);

(async () => {
  browser = await chromium.launch();
  context = await browser.newContext({ permissions: ['clipboard-read', 'clipboard-write'] });
  await context.route('https://shiduchim.github.io/**', routeSite);
  await context.route(/https:\/\/script\.google\.com\/.*/, routeGoogle);

  await test('Contract: every question/option on the page is accepted by Code.gs (v2)', async () => {
    const page = await open('en');
    const qs = await page.evaluate(() => dict.en.questions);
    const server = JSON.parse(JSON.stringify(gas.QUESTIONS.v2));   // copy out of the vm context
    for (const q of qs) {
      assert.ok(server[q.id], 'server missing question ' + q.id);
      if (q.type === 'text') { assert.strictEqual(server[q.id].type, 'text'); continue; }
      assert.strictEqual(server[q.id].type, q.type, q.id);
      assert.deepStrictEqual(server[q.id].options, q.options, q.id + ' codes and English labels');
      assert.strictEqual(server[q.id].limit, q.max, q.id + ' limit');
      if (q.options.some(o => o[0] === 'other')) assert.ok(server[q.id + '_other'], q.id + '_other');
    }
    assert.deepStrictEqual(Object.keys(server).sort(), qs.flatMap(q => (q.options || []).some(o => o[0] === 'other') ? [q.id, q.id + '_other'] : [q.id]).sort());
    for (const q of qs) for (const [code] of q.options || []) {
      const ans = { [q.id]: q.type === 'checks' ? [code] : code };
      const r = JSON.parse(gas.doPost({ postData: { contents: JSON.stringify({ surveyVersion: 'v2', submissionId: 'contract_' + q.id + '_' + code, language: 'en', answers: ans }) } }).content);
      assert.ok(r.ok, `rejected ${q.id}=${code}: ${r.error}`);
    }
    grid = grid.filter(r => !String(r[1]).startsWith('contract_'));
    await page.close();
  });

  await test('Branding: ZugDesk everywhere visible, no ZugPad, in all six languages', async () => {
    const page = await open('en');
    assert.strictEqual(await page.title(), 'ZugDesk — A short survey for shadchanim');
    assert.strictEqual(await page.textContent('.logo'), 'ZugDesk');
    for (const p of ['og:title', 'og:site_name', 'og:image:alt']) assert.match(await page.getAttribute(`meta[property="${p}"]`, 'content'), /ZugDesk/);
    assert.strictEqual(await page.getAttribute('meta[property="og:image"]', 'content'), PAGE + 'zugdesk-preview.png');
    assert.ok(await page.evaluate(u => fetch(u).then(r => r.ok && r.headers.get('content-type') === 'image/png'), PAGE + 'zugdesk-preview.png'));
    // Internal identifiers (zugpadExtraTranslations, zugpad_status) are kept on purpose.
    const visible = await page.evaluate(() => document.title + document.body.innerText +
      [...document.querySelectorAll('meta')].map(m => m.content).join(' '));
    assert.ok(!/ZugPad/i.test(visible), 'ZugPad visible on page');
    assert.ok(!/ZugPad/.test(await page.content()), 'ZugPad brand text in HTML');
    for (const lang of ['en', 'he', 'yi', 'ru', 'fr', 'es']) {
      const d = await page.evaluate(l => dict[l], lang);
      const all = JSON.stringify(d);
      assert.ok(!/ZugPad/i.test(all), 'ZugPad in ' + lang);
      assert.match(d.intro, /ZugDesk/, 'intro ' + lang);
      assert.match(d.shareMessage, /ZugDesk/, 'share ' + lang);
      assert.strictEqual(d.questions.length, 6, lang);
      assert.strictEqual(d.questions[2].options.length, 9, 'Q3 options ' + lang);
    }
    await page.close();
  });

  await test('1. Normal English submission', async () => {
    const page = await open('en');
    await pick(page, 'tools', 'wa'); await pick(page, 'tools', 'notes');
    await page.fill('textarea[name="incident"]', 'A follow-up was forgotten for two weeks.');
    await pick(page, 'friction', 'followup');
    const id = await lastId(page);
    await submitAndWaitThanks(page);
    const r = newRows().find(x => x.submissionId === id);
    assert.ok(r, 'row saved');
    assert.strictEqual(r.language, 'en'); assert.strictEqual(r.tools, 'WhatsApp or other messages; A notes app (Google Keep, Apple Notes, etc.)');
    assert.strictEqual(r.incident, 'A follow-up was forgotten for two weeks.'); assert.strictEqual(r.friction, 'Remembering whom to contact or follow up with');
    assert.strictEqual(r.surveyVersion, 'v2');
    await page.close();
  });

  await test('2. Hebrew submission (RTL)', async () => {
    const page = await open('he');
    assert.strictEqual(await page.getAttribute('html', 'dir'), 'rtl');
    await pick(page, 'tools', 'paper');
    await page.fill('textarea[name="incident"]', 'שכחתי לחזור למשפחה');
    const id = await lastId(page);
    await submitAndWaitThanks(page);
    assert.strictEqual(await page.textContent('.thankyou h2'), 'תודה! התשובות התקבלו ונשמרו.');
    const r = newRows().find(x => x.submissionId === id);
    assert.strictEqual(r.language, 'he'); assert.strictEqual(r.incident, 'שכחתי לחזור למשפחה');
    await page.close();
  });

  for (const lang of ['ru', 'yi', 'fr', 'es']) await test(`3. ${lang} submission`, async () => {
    const page = await open(lang);
    await pick(page, 'barrier', 'privacy');
    const id = await lastId(page);
    await submitAndWaitThanks(page);
    const r = newRows().find(x => x.submissionId === id);
    assert.strictEqual(r.language, lang); assert.strictEqual(r.barrier, 'Privacy or confidentiality concerns');
    await page.close();
  });

  await test('4. Only one question answered', async () => {
    const page = await open('en');
    await pick(page, 'device_current', 'phone');
    const id = await lastId(page);
    await submitAndWaitThanks(page);
    const r = newRows().find(x => x.submissionId === id);
    assert.strictEqual(r.device_current, 'Mainly my phone'); assert.strictEqual(r.tools, ''); assert.strictEqual(r.incident, '');
    await page.close();
  });

  await test('4b. Empty form is blocked on the page (nothing sent)', async () => {
    const page = await open('en'); const before = posts;
    await page.click('button.submit');
    await page.waitForSelector('#message.show');
    assert.match(await page.textContent('#message'), /answer at least one/);
    assert.strictEqual(posts, before);
    await page.close();
  });

  await test('5. "Something else" with its text boxes (incl. formula-like text)', async () => {
    const page = await open('en');
    assert.ok(await page.isHidden('#tools_other'));
    await pick(page, 'tools', 'other');
    await page.fill('#tools_other', '=HYPERLINK("x") my own system');
    await pick(page, 'friction', 'other'); await page.fill('#friction_other', 'Finding time');
    await pick(page, 'barrier', 'other'); await page.fill('#barrier_other', 'Cost');
    const id = await lastId(page);
    await submitAndWaitThanks(page);
    const r = newRows().find(x => x.submissionId === id);
    assert.strictEqual(r.tools, 'Something else'); assert.strictEqual(r.tools_other, `'=HYPERLINK("x") my own system`);
    assert.strictEqual(r.friction_other, 'Finding time'); assert.strictEqual(r.barrier_other, 'Cost');
    await page.close();
  });

  await test('6. Question 3 with two selections; a third is refused', async () => {
    const page = await open('en');
    await pick(page, 'friction', 'candidates'); await pick(page, 'friction', 'duplicates');
    await page.click('input[name="friction"][value="status"]');
    assert.strictEqual(await page.isChecked('input[name="friction"][value="status"]'), false);
    assert.match(await page.textContent('#message'), /no more than two/);
    const id = await lastId(page);
    await submitAndWaitThanks(page);
    assert.strictEqual(newRows().find(x => x.submissionId === id).friction, 'Finding suitable people to suggest; Avoiding repeated or duplicate suggestions');
    await page.close();
  });

  await test('7. "None" selections (Q3, Q6 and workload)', async () => {
    const page = await open('en');
    await pick(page, 'barrier', 'privacy'); await pick(page, 'barrier', 'none');
    assert.strictEqual(await page.isChecked('input[name="barrier"][value="privacy"]'), false);
    await pick(page, 'friction', 'none'); await pick(page, 'workload', 'none');
    const id = await lastId(page);
    await submitAndWaitThanks(page);
    const r = newRows().find(x => x.submissionId === id);
    assert.strictEqual(r.barrier, 'Nothing in particular'); assert.strictEqual(r.friction, 'None of these'); assert.strictEqual(r.workload, 'None');
    await page.close();
  });

  await test('8. Workload and device answers', async () => {
    const page = await open('en');
    await pick(page, 'workload', '21_50'); await pick(page, 'device_current', 'both');
    const id = await lastId(page);
    await submitAndWaitThanks(page);
    const r = newRows().find(x => x.submissionId === id);
    assert.strictEqual(r.workload, '21–50'); assert.strictEqual(r.device_current, 'Phone and computer about equally');
    await page.close();
  });

  await test('9a. Saved but reply lost: page confirms via status lookup, one row', async () => {
    const page = await open('en');
    await pick(page, 'tools', 'email');
    const id = await lastId(page);
    mode = 'lostReply';
    await submitAndWaitThanks(page);
    mode = 'normal';
    assert.strictEqual(newRows().filter(x => x.submissionId === id).length, 1);
    await page.close();
  });

  await test('9b. Same submission ID sent twice: one row, second reply ok:true', async () => {
    const body = JSON.stringify({ surveyVersion: 'v2', submissionId: 'sRETRY0000001', language: 'fr', answers: { workload: '6_20' } });
    const a = JSON.parse(gas.doPost({ postData: { contents: body } }).content);
    const b = JSON.parse(gas.doPost({ postData: { contents: body } }).content);
    assert.deepStrictEqual(a, { ok: true }); assert.deepStrictEqual(b, { ok: true, duplicate: true });
    assert.strictEqual(newRows().filter(x => x.submissionId === 'sRETRY0000001').length, 1);
  });

  await test('10a. Phone offline: clear error, answers kept, retry after reconnect saves once', async () => {
    const page = await open('en');
    await pick(page, 'workload', '1_5'); await page.fill('textarea[name="incident"]', 'Offline test');
    const id = await lastId(page);
    await context.setOffline(true);
    assert.match(await submitAndWaitError(page), /Could not save your answers/);
    assert.ok(await page.isChecked('input[name="workload"][value="1_5"]'));
    assert.strictEqual(await page.inputValue('textarea[name="incident"]'), 'Offline test');
    assert.strictEqual(newRows().filter(x => x.submissionId === id).length, 0);
    await context.setOffline(false);
    await submitAndWaitThanks(page);
    assert.strictEqual(newRows().filter(x => x.submissionId === id).length, 1);
    await page.close();
  });

  await test('10b. Server unreachable: error (no false success), then recovery saves once', async () => {
    const page = await open('he');
    await pick(page, 'device_current', 'desktop');
    const id = await lastId(page);
    mode = 'down';
    assert.match(await submitAndWaitError(page), /לא הצלחנו לשמור/);
    assert.strictEqual(await page.$('.thankyou'), null);
    mode = 'normal';
    await submitAndWaitThanks(page);
    assert.strictEqual(newRows().filter(x => x.submissionId === id).length, 1);
    await page.close();
  });

  await test('10c. Server rejects the answers: error shown, nothing saved', async () => {
    const page = await open('en');
    await page.evaluate(() => { answers.workload = 'bogus'; });
    const id = await lastId(page);
    assert.match(await submitAndWaitError(page), /Could not save/);
    assert.strictEqual(newRows().filter(x => x.submissionId === id).length, 0);
    await page.close();
  });

  await test('11. Thank-you screen, WhatsApp and Copy link', async () => {
    const page = await open('es');
    await pick(page, 'tools', 'memory');
    await submitAndWaitThanks(page);
    await page.evaluate(() => { window.__opened = []; window.open = (u) => { window.__opened.push(u); }; });
    await page.click('.share-button.whatsapp');
    const url = decodeURIComponent((await page.evaluate(() => window.__opened[0])).split('text=')[1]);
    assert.ok(url.startsWith('ZugDesk: una breve encuesta'), url);
    assert.ok(url.endsWith('\n' + PAGE), url);
    await page.click('.share-button:not(.whatsapp)');
    await page.waitForFunction(() => document.querySelector('.share-status').textContent.length > 0);
    assert.strictEqual(await page.evaluate(() => navigator.clipboard.readText()), PAGE);
    await page.click('#en');           // language switch keeps the thank-you screen
    assert.ok(await page.$('.thankyou'));
    await page.close();
  });

  await test('Mobile layout (390px) renders without horizontal scroll', async () => {
    const page = await context.newPage();
    await page.setViewportSize({ width: 390, height: 800 });
    await page.goto(PAGE);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= 390));
    await page.close();
  });

  await test('v1 page cached in a browser: saved with the original labels, Preferred device column', async () => {
    const body = JSON.stringify({ submissionId: 'sV1CACHED0001', language: 'he',
      answers: { tools: ['wa', 'memory'], feature: 'search', device: 'both', barrier: ['noapp'] } });
    assert.deepStrictEqual(JSON.parse(gas.doPost({ postData: { contents: body } }).content), { ok: true });
    const r = newRows().find(x => x.submissionId === 'sV1CACHED0001');
    assert.strictEqual(r.tools, 'WhatsApp or messages; Mostly memory'); assert.strictEqual(r.feature, 'Find and filter people quickly');
    assert.strictEqual(r.device, 'Both phone and computer'); assert.strictEqual(r.barrier, 'I don’t want another app');
    assert.strictEqual(r.surveyVersion, 'v1'); assert.strictEqual(r.device_current, '');
  });

  await test('A sheet missing one column gets it added once, at the right', async () => {
    const saved = grid;
    grid = [LEGACY_HEADERS.filter(h => h !== 'Survey version')];
    for (const id of ['sMISSING00001', 'sMISSING00002']) {
      const body = JSON.stringify({ surveyVersion: 'v2', submissionId: id, language: 'en', answers: { workload: '6_20' } });
      assert.ok(JSON.parse(gas.doPost({ postData: { contents: body } }).content).ok);
    }
    assert.deepStrictEqual(grid[0], [...LEGACY_HEADERS.filter(h => h !== 'Survey version'), 'Survey version']);
    assert.strictEqual(grid.length, 3);
    grid = saved;
  });

  await test('Sheet: all 16 columns recognized, old row untouched, no column added', async () => {
    assert.deepStrictEqual(grid[0].slice(0, LEGACY_HEADERS.length), LEGACY_HEADERS);
    assert.deepStrictEqual(grid[1], LEGACY_ROW);
    assert.deepStrictEqual(grid[0], LEGACY_HEADERS, 'no column added');
    const check = [...gas.checkColumns()].join('\n');
    for (const [key, col, name] of [['timestamp', 1, 'Submitted at'], ['tools', 5, 'Current tools'], ['friction', 7, 'Difficult tasks'],
      ['incident', 9, 'Recent example'], ['device', 12, 'Preferred device'], ['barrier', 13, 'Adoption barriers'],
      ['workload', 15, 'Recent workload (v2)'], ['device_current', 16, 'Current device used (v2)']])
      assert.ok(check.includes(`${key} -> column ${col} "${name}"`), key + '\n' + check);
    assert.ok(check.includes('16 of 16 fields found in existing columns; nothing will be added'), check);
    assert.ok(!/NEW|SAME COLUMN/.test(check), check);
    const ids = newRows().map(r => r.submissionId);
    assert.strictEqual(new Set(ids).size, ids.length, 'duplicate rows');
  });

  await browser.close();
  console.log('\nSheet after tests (' + grid.length + ' rows incl. header):');
  console.log(grid[0].join(' | '));
  console.table(rows().map(r => ({ ...r, timestamp: String(r.timestamp).slice(0, 15) })));
  const failed = results.filter(r => r[0] === 'FAIL').length;
  console.log(`\n${results.length - failed}/${results.length} passed`);
  process.exit(failed ? 1 : 0);
})();
