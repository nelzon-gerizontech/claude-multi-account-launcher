# Mac version (preview)

The Mac version does the same job as the Windows app: one tab per Claude account, each with its own sign-in. It is built automatically by GitHub on Apple's hardware every time the code changes, and a self-test runs on a real Mac before each build is kept.

It is a **preview**. Please report anything that looks or behaves wrong.

## Requirements

- macOS 12 Monterey or later
- Apple chip Mac (M1 or later) or Intel Mac

## Download

1. Open the repository's **Releases** page and find the newest "for Mac" release.
2. Download the file for your Mac:
   - Apple chip (M1, M2, M3, M4 and later): `ClaudeMultiAccountLauncher-<version>-mac-arm64.dmg`
   - Intel: `ClaudeMultiAccountLauncher-<version>-mac-x64.dmg`

   Not sure which? Click the Apple menu, then **About This Mac**. "Chip: Apple M…" means Apple chip. "Processor: Intel" means Intel.

## Install and first open

1. Open the `.dmg` and drag **Claude Multi-Account Launcher** into **Applications**.
2. Open it from Applications. Because this build is not yet signed with an Apple Developer ID, macOS blocks it the first time:
   - **macOS 15 Sequoia or later:** click **Done** on the warning. Go to **System Settings > Privacy & Security**, scroll down to the message about Claude Multi-Account Launcher, click **Open Anyway**, then confirm with your password.
   - **macOS 12 to 14:** right-click (or Control-click) the app, choose **Open**, then click **Open** again.
3. You only need to do this once. After that it opens normally.

If macOS says the app "is damaged", run this once in Terminal, then open the app again:

```
xattr -dr com.apple.quarantine "/Applications/Claude Multi-Account Launcher.app"
```

## Using it

- Click **+** to add an account, then sign in inside its tab.
- Right-click a tab to rename, move, reload or delete it. Double-click a tab to rename it.
- An orange dot means that tab is not signed in yet.
- If a tab ends up on another company's page or an error (for example while connecting a connector), click **Back to Claude** in the blue strip, or use the ← and ⌂ buttons.
- Shortcuts: ⌘T add account, ⌘[ go back, ⌘⇧H back to Claude home, ⌘1 to ⌘9 jump to a tab, Ctrl+Tab next tab, ⌘R reload.
- If Google sign-in is refused, sign in with your email. When the sign-in email arrives, copy its link, then choose **Paste sign-in link** from the ⋯ menu.

Data is kept in `~/Library/Application Support/Claude Multi-Account Launcher`. Deleting an account in the app removes its data there.

## Test checklist for reviewers

Please try each item and note what happened:

- [ ] The app opens after the first-open steps above.
- [ ] Add two accounts and sign in to a different Claude account in each.
- [ ] Switching tabs shows the right account each time.
- [ ] Quit the app (⌘Q) and open it again: both tabs are still signed in.
- [ ] Rename, move and delete a tab.
- [ ] Copy and paste text in a chat (⌘C / ⌘V).
- [ ] Upload a file to a chat, and download a file Claude makes.
- [ ] Voice input asks for microphone permission and works (optional).
- [ ] A link in a chat opens in your normal browser.

When reporting a problem, include your macOS version, Apple chip or Intel, and a screenshot.

## Signing (later)

To remove the first-open warning permanently, the app needs an Apple Developer ID certificate (Apple Developer Program, US$99 per year) and notarization. The build is set up so that signing can be added to the GitHub workflow once an account exists.

## Build it yourself

```
cd electron
npm ci
npm start            # run from source
npm run dist:mac     # build .dmg files (on a Mac)
```
