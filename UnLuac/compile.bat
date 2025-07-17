@echo off
setlocal enabledelayedexpansion
set SOURCES=
for /R src %%f in (*.java) do (
  set SOURCES=!SOURCES! "%%f"
)
javac -cp .\class -d .\class -sourcepath src !SOURCES!
