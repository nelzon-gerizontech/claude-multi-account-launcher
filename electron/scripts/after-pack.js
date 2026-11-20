// Gives the Mac app an "ad-hoc" signature after packing.
// Apple chip Macs refuse to open apps with no signature at all (they report the
// app as "damaged"). An ad-hoc signature is free and fixes that. It does not
// remove the "unidentified developer" warning; that needs an Apple Developer ID.
const { execFileSync } = require('child_process');
const path = require('path');

exports.default = async function afterPack(context) {
  if (context.electronPlatformName !== 'darwin') return;
  const appName = context.packager.appInfo.productFilename + '.app';
  const appPath = path.join(context.appOutDir, appName);
  console.log('  • ad-hoc signing ' + appPath);
  execFileSync('codesign', ['--force', '--deep', '--sign', '-', appPath], { stdio: 'inherit' });
  execFileSync('codesign', ['--verify', '--deep', '--strict', appPath], { stdio: 'inherit' });
};
