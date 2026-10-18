# ZugDesk survey backend

`Code.gs` is the Google Apps Script web app that saves survey answers from
`survey/index.html` into the **Responses** tab of the Google Sheet.

GitHub does not deploy this file. It must be pasted into Apps Script and
deployed as a **new version of the existing deployment**, so the `/exec` URL
in `survey/index.html` stays the same.

## Deploy (about 5 minutes)

1. Open the Google Sheet → **Extensions → Apps Script** (or open the project
   at script.google.com that owns the deployment ending in `...MxURuw/exec`).
2. **Back up the current code:** select all of `Code.gs`, copy it into a text
   file and keep it (also send it to the developer if the problem persists).
3. Replace the whole contents of `Code.gs` with this repository's
   `survey-backend/Code.gs`. Click **Save**.
   - If the script is *not* attached to the Sheet (it was created separately at
     script.google.com), set `SPREADSHEET_ID` at the top to the ID from the
     Sheet's URL (`docs.google.com/spreadsheets/d/<ID>/edit`).
4. Optional check: choose **testSave** in the function list → **Run**. Approve
   access if asked. The log should show `"first":{"ok":true}` and
   `"retry":{"ok":true,"duplicate":true}`, and the Sheet gets one row whose
   Submission ID starts with `selftest-`. Delete that row.
5. **Deploy → Manage deployments** → select the existing **Web app**
   deployment → pencil icon (**Edit**) → **Version: New version** →
   **Deploy**.
   - Execute as: **Me**. Who has access: **Anyone**.
   - Do **not** use *New deployment*: that creates a different URL and the
     survey would keep calling the old code.
6. Confirm the Web app URL shown is still
   `https://script.google.com/macros/s/AKfycbwj-CnzBCJSxC-vU-z42cEYijvzH9uCLbwGgIEuvz-NKngNGYEDaQHMYalz2RaSxMURuw/exec`.

Saving the code alone does **not** change what the live survey runs; only
step 5 does.

## Columns

Columns are found by the header text in row 1 (case, spaces and underscores
are ignored, so `Submission ID` and `submissionId` match). Existing columns
and responses are never moved or overwritten. Any column the code needs that
is missing is added at the right end of row 1. With the second survey version
these are typically `surveyVersion`, `workload` and `device_current`.

Answers are stored as option codes (for example `wa, notes`, `6_20`), the
same codes in every language; the `language` column shows which language was
used.

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
