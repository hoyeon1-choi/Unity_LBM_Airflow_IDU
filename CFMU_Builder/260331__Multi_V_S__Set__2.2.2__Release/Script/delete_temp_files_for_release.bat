@echo off

echo **** Delete visual studio cache files ****
forfiles /P "..\.vs" /S /M "ipch" /C "cmd /c IF @isdir==TRUE @echo [delete] @relpath" 2>nul
forfiles /P "..\.vs" /S /M "ipch" /C "cmd /c IF @isdir==TRUE @rmdir /S /Q @path" 2>nul
forfiles /P "..\.vs" /S /M "*.db" /C "cmd /c @echo [delete] @file" 2>nul
forfiles /P "..\.vs" /S /M "*.db" /C "cmd /c @del /Q @file" 2>nul
echo:

echo **** Delete debug file (*.pdb) ****
forfiles /P "..\Library\LIB_CFMU_Manager" /S /M "*.pdb" /C "cmd /c @echo [delete] @file" 2>nul
forfiles /P "..\Library\LIB_CFMU_Manager" /S /M "*.pdb" /C "cmd /c @del /Q @file" 2>nul

echo **** Delete project folders (compile output) ****
if exist "..\Bin" (
	echo [delete] ..\Bin
	rd /Q /S "..\Bin"
)
if exist "..\Build" (
	echo [delete] ..\Build
	rd /Q /S "..\Build"
)

echo **** Delete CFMU files ****
for /D %%A in (..\CFMU_*) do (
	echo [delete] %%A
	rd /Q /S "%%A"
)

echo:

pause
@echo on
