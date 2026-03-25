@echo off
title MazerunnerAI - ML-Agents Training
echo ============================================
echo   MazerunnerAI - ML-Agents Training
echo ============================================
echo.

:: Check if mlagents is installed
where mlagents-learn >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] mlagents-learn not found.
    echo.
    echo Install it with:
    echo   pip install mlagents
    echo.
    pause
    exit /b 1
)

:: Set run ID with timestamp so runs don't overwrite each other
for /f "tokens=2 delims==" %%I in ('wmic os get localdatetime /value') do set datetime=%%I
set TIMESTAMP=%datetime:~0,8%_%datetime:~8,4%
set RUN_ID=MazeChaser_%TIMESTAMP%

echo Run ID: %RUN_ID%
echo Config: Assets/Training/maze_training.yaml
echo.
echo When you see "Listening on port 5004", press PLAY in Unity.
echo Press Ctrl+C to stop training.
echo.
echo ============================================
echo.

mlagents-learn Assets/Training/maze_training.yaml --run-id=%RUN_ID%

echo.
echo ============================================
echo Training complete!
echo.
echo Your trained model is in: results\%RUN_ID%\
echo Copy the .onnx file into Unity and assign it
echo to the Enemy's Behavior Parameters > Model field.
echo ============================================
pause
