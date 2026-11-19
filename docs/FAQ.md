# FAQ

## Does it stay signed in after I close the app or restart my PC?

Yes. Each account's sign-in is saved in its own folder on your PC, just as a browser saves it. The tab list, its order and the last tab you used are saved too. Claude may occasionally ask you to sign in again, just as it does in a browser.

## Can it run Chrome extensions?

Not in this version. Microsoft's WebView2 engine can load an extension only from an unpacked folder on the PC. There's no Chrome Web Store, and extensions that use a toolbar button or pop-up lose those, because there is no browser toolbar to show them in.

Claude in Chrome needs the real Chrome browser, so it can't run here at all. Keep using Chrome for that.

If you have a specific extension in mind, open an issue. Loading an unpacked extension per account is possible to add.

## Does "Delete account" delete my Claude account?

No. It removes the tab, signs the account out on this PC and deletes its local data. The Claude account and its chats stay online. To close a Claude account itself, use Claude's own settings.

## Why does Windows warn me when I open it?

Until a release is code-signed and has built up a reputation, Windows SmartScreen may show "Windows protected your PC". Click **More info**, then **Run anyway**. See [CODE_SIGNING.md](CODE_SIGNING.md).

## Why isn't Google sign-in working?

Google often blocks sign-in inside embedded browsers. Use email sign-in instead.
