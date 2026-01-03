@echo off
echo Installing DesktopAgent v2.0...

REM Create virtual environment
python -m venv venv
call venv\Scripts\activate

REM Upgrade pip
python -m pip install --upgrade pip

REM Install requirements
pip install -r requirements.txt

REM Create necessary directories
mkdir agent_v2\monitor
mkdir agent_v2\generator
mkdir agent_v2\analyzer
mkdir agent_v2\fixer
mkdir agent_v2\cli
mkdir agent_v2\validator
mkdir logs
mkdir temp

REM Copy config template
if exist config.yaml (
  echo Config already exists.
) else (
  copy config.yaml.template config.yaml
)

echo.
echo Installation complete!
echo Run 'python run_assistant.py --help' to get started
pause
