# ZugDesk survey backend

`Code.gs` is the Google Apps Script web app that saves survey answers from
`survey/index.html` into the **Responses** tab of the Google Sheet.

GitHub does not deploy this file. It must be pasted into Apps Script and
deployed as a **new version of the existing deployment**, so the `/exec` URL
in `survey/index.html` stays the same.

## Deploy (about 5 minutes)

1. Open the Apps Script project that owns the deployment ending in
   `...MxURuw/exec`.
2. **Back up the current code:** copy all of `Code.gs` into a text file and
   keep it. Note the Google Sheets URL it uses.
3. Replace the whole contents of `Code.gs` with this repository's
   `survey-backend/Code.gs`. On the line
   `var SPREADSHEET_URL = 'PASTE_YOUR_GOOGLE_SHEET_URL_HERE';` paste that
   same Sheet URL between the quotes. Click **Save**.
4. Choose **checkColumns** in the function list → **Run** (approve access if
   asked). It writes nothing and logs one line for each of the 16 v1 and v2
   fields, for example `tools -> column 5 "Current tools"`, then a summary.
   The last line should read
   `16 of 16 fields found in existing columns; nothing will be added`. A line marked `NEW` means no matching header was found: add that
   column's exact header text to the field's list in `HEADERS` (near the top),
   Save, and run **checkColumns** again until only intended new columns show
   `NEW`. A line marked `SAME COLUMN AS` means two fields matched one
   column; fix that before deploying.
5. Choose **testSave** → **Run**. The log should show `"first":{"ok":true}`
   and `"retry":{"ok":true,"duplicate":true}`, and the Sheet gets exactly one
   row whose Submission ID starts with `selftest-`, with values in the right
   columns. Delete that row.
6. **Deploy → Manage deployments** → select the existing **Web app**
   deployment → pencil icon (**Edit**) → **Version: New version** →
   **Deploy**. Execute as: **Me**. Who has access: **Anyone**.
   Do **not** use *New deployment*: that creates a different URL.
7. Confirm the Web app URL is still
   `https://script.google.com/macros/s/AKfycbwj-CnzBCJSxC-vU-z42cEYijvzH9uCLbwGgIEuvz-NKngNGYEDaQHMYalz2RaSxMURuw/exec`.

Saving the code alone does **not** change what the live survey runs; only
step 6 does.

## Columns

Each field is written to the column whose row-1 header matches one of its
names in `HEADERS` (case, spaces and punctuation are ignored). Existing
columns and responses are never moved or overwritten. Only a field with no
matching header gets a new column at the right end of row 1, and only once.

Answers are stored as the English answer text that respondents saw (for
example `WhatsApp or other messages; Email`), whatever language they used,
like the earlier backend; the language column shows which language was used.
Several choices in one answer are separated by `LABEL_SEPARATOR` (`; `),
because some answer texts contain commas. Historical rows are not changed.

## If saving fails after deploying

In Apps Script open **Executions**, find the failed `doPost`, and send its
error text. In the browser, the developer console also shows
`Survey server did not save: <reason>` when the server rejects a submission.

## Tests

`test/simulated-e2e.js` runs the real survey page in Chromium against this
`Code.gs` with an in-memory sheet (Google is not contacted):

```
NODE_PATH=$(npm root -g) node survey-backend/test/simulated-e2e.js
```
