| Test | Result | Detail |
|---|---|---|
| Silent install (Program Files, admin) | PASS | exit 0 in 7 s |
| Apps & features entry | PASS | EK FastPDF 1.2.0 |
| Start menu shortcut | PASS | C:\ProgramData\Microsoft\Windows\Start Menu\Programs\EK FastPDF\EK FastPDF.lnk |
| Desktop shortcut (task selected) | PASS | C:\Users\Public\Desktop\EK FastPDF.lnk |
| ProgID registered | PASS | "C:\Program Files\EK FastPDF\EKFastPDF.exe" "%1" |
| Listed in Open with (.pdf OpenWithProgids) | PASS |  |
| Default Apps capability registered | PASS | Software\EK\EK FastPDF\Capabilities |
| Default PDF app unchanged | PASS | before 'MSEdgePDF' after 'MSEdgePDF' |
| Installed exe has no Mark-of-the-Web | PASS |  |
| Direct start from install folder | PASS | window in 677 ms; title 'EK FastPDF' |
| Explorer start of EKFastPDF.exe (double-click path) | PASS | window in 654 ms; title 'EK FastPDF' |
| ShellExecute of EKFastPDF.exe (cwd C:\Windows) | PASS | window in 434 ms; title 'EK FastPDF' |
| Start menu shortcut | PASS | window in 427 ms; title 'EK FastPDF' |
| Desktop shortcut | PASS | window in 423 ms; title 'EK FastPDF' |
| App Paths (Win+R "EKFastPDF") | PASS | window in 433 ms; title 'EK FastPDF' |
| Quoted Unicode path with spaces, other working dir | PASS | window in 433 ms; title 'E-101 Power Plan – Level 1 (rev Ø).pdf – EK FastPDF' |
| Open with: EKFastPDF.Document ProgID (file association) | PASS | window in 421 ms; title 'E-101 Power Plan – Level 1 (rev Ø).pdf – EK FastPDF' |
| Applications\EKFastPDF.exe registration (Choose another app) | PASS | "C:\Program Files\EK FastPDF\EKFastPDF.exe" "%1" |
| Single instance: second file opens as tab in same window | PASS | main windows: 1; title 'second document.pdf – EK FastPDF' |
| Render worker processes running | PASS | 2 workers |
| No processes left after closing | PASS | 0 remaining |
| Software rendering fallback (--software-rendering) | PASS | window in 441 ms; title 'second document.pdf – EK FastPDF' |
| Software mode reported in log | PASS |  |
| Corrupt PDF shows an error tab, no crash | PASS | window in 448 ms; title 'broken.pdf – EK FastPDF' |
| Viewer still running after corrupt file | PASS |  |
| Diagnostics report (--diagnose) | PASS | Security zone (exe)       : 0 Local machine Mark-of-the-Web           : none CPU                       : 4 logical / 2 cores, 16 GB RAM GPU adapter               : Microsoft Basic Render Driver (vendor 0x1414, 0 MB dedicated, 8189 MB shared) GPU adapter               : Microsoft Basic Render Driver (vendor 0x1414, 0 MB dedicated, 8189 MB shared, software) |
| Vector printing (Microsoft Print to PDF) | PASS | exit 0, 3413039 bytes |
| Upgrade / reinstall over existing installation | PASS | exit 0 |
| Upgrade kept a single install folder | PASS |  |
| Start menu shortcut after upgrade | PASS | window in 760 ms; title 'EK FastPDF' |
| Uninstall | PASS | exit 0 |
| Uninstall removed shortcuts | PASS |  |
| Uninstall removed ProgID | PASS |  |
| Uninstall removed Open with entry | PASS |  |
| Uninstall removed Apps & features entry | PASS |  |
