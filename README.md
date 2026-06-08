# TokenDock

TokenDock is a Windows desktop usage monitor for Codex and Claude. It shows remaining usage limits, refresh status, and compact widget views so you can keep an eye on AI usage while working.

## What It Does

- Codex usage dashboard with 5-hour and weekly limit status
- Claude usage dashboard with session and weekly limit status
- Compact floating widget with multiple display modes
- Usage alerts when remaining limits get low
- OpenAI account connection for Codex usage lookup
- Claude CLI OAuth credential lookup for Claude usage

## How To Use

- Open the `Codex` tab to connect your OpenAI account and view Codex usage.
- Open the `Claude` tab to view Claude usage from local Claude CLI credentials.
- Open `Settings` to choose which services are shown.
- Enable the mini widget to keep usage status visible while working.
- Use the refresh button when you want to update usage immediately.

## Run From Source

Requirements:

- Windows
- .NET 9 SDK

Clone the project:

```powershell
git clone https://github.com/lukewire129/TokenDock.git
cd TokenDock
```

Restore dependencies:

```powershell
dotnet restore TokenDock.sln
```

Run locally:

```powershell
dotnet run --project src/TokenDock/TokenDock.csproj
```

Run tests:

```powershell
dotnet test TokenDock.sln
```
