#!/bin/sh
# Builds ClaudeMultiAccountLauncher.exe on Linux/macOS with Mono and LLVM.
# Needs: mono (mcs), llvm-windres, and the Microsoft.Web.WebView2 1.0.3856.49 DLLs in ./lib
# (from the NuGet package: lib/net462/*.dll and runtimes/*/native/WebView2Loader.dll).
set -e
cd "$(dirname "$0")"
mkdir -p build
(cd src && llvm-windres-18 --no-preprocess app.rc -O res -o ../build/app.res 2>/dev/null || llvm-windres --no-preprocess app.rc -O res -o ../build/app.res)
mcs -sdk:4.5 -target:winexe -optimize+ -out:build/ClaudeMultiAccountLauncher.exe -win32res:build/app.res \
  -resource:assets/gerizon-mark.png,gerizon-mark.png -resource:assets/gerizon-logo-64.png,gerizon-logo-64.png \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Runtime.Serialization.dll -r:System.Core.dll \
  -r:lib/Microsoft.Web.WebView2.Core.dll -r:lib/Microsoft.Web.WebView2.WinForms.dll src/Program.cs
echo "Built build/ClaudeMultiAccountLauncher.exe"
