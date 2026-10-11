$agentRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
& "$agentRoot\.venv\Scripts\python.exe" -m uvicorn main:app --host 127.0.0.1 --port 8100 --reload --app-dir $agentRoot
