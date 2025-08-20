# FacebookChecker (Windows Forms, .NET 8 - net8.0-windows target)

This package contains a ready-to-build GUI app that checks Facebook pages' `/reviews/` endpoints in batches.
This variant includes retry logic and optional proxy rotation.

## Build (creates a portable EXE)

1. Install [.NET 8 SDK] on Windows and install the windows desktop workload if not already:
   ```
   dotnet workload install windowsdesktop
   ```
2. Extract this package and open the folder `FacebookChecker\FacebookChecker` in PowerShell or CMD.
3. Run `publish-win-x64.bat`. The script will produce `dist\FacebookChecker.exe` (self-contained, single file).

## Usage

- `list.txt`: one username/ID per line (e.g., `cocacola`).
- `cookies.txt`: optional. If present, its content will be sent as the `Cookie` header. If omitted, requests are sent without cookies.
- `proxies.txt`: optional. One proxy per line. Example formats accepted by the app: `http://host:port`, `http://user:pass@host:port`. If present, the app will pick a random proxy for each request.
- Results: `live.txt`, `dead.txt`, `log.txt` appear in the working directory.

Note: Use responsibly and respect Facebook Terms of Service and local laws.
