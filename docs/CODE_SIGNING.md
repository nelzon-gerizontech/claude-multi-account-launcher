# Code signing

Signed releases show **Gerizon Information Technology Solutions** (or the signer named below) as the verified publisher, and Windows and antivirus tools trust them more. A publicly trusted certificate has to be bought from, or issued by, a certificate authority after it verifies who you are. It can't be created inside the project.

## Options

| Option | Publisher shown | Cost | Notes |
|---|---|---|---|
| **Standard (OV) code signing**, for example Certum Standard Code Signing in the Cloud | Gerizon Information Technology Solutions | Paid yearly | Needs business verification (DTI registration and similar). The key lives in the CA's cloud signing service or on a hardware token. Since 27 February 2026, one certificate lasts at most 459 days. |
| **Certum Open Source Code Signing** | "Open Source Developer, <your name>" | Lower cost | Issued to a person, not a company. |
| **SignPath Foundation** (free for open source) | SignPath Foundation | Free | Needs a public repository, an OSI license (MIT qualifies), builds in a CI service such as GitHub Actions, multi-factor authentication for the team, and a published signing policy. |
| **Self-signed certificate** pushed by your IT to managed PCs | Gerizon (trusted only on those PCs) | Free | Fine for internal use only. Outside PCs still show warnings. |

Even signed releases may get a SmartScreen warning until the publisher builds a reputation.

## Signing a release (Windows)

The release `.exe` is the only file to sign. The bundled Microsoft DLLs already carry Microsoft's signatures.

With a certificate installed (cloud or token):

```powershell
.\scripts\sign.ps1 -Exe .\build\ClaudeMultiAccountLauncher.exe -Thumbprint <certificate thumbprint>
```

Then check it:

```powershell
Get-AuthenticodeSignature .\build\ClaudeMultiAccountLauncher.exe | Format-List Status, SignerCertificate
```

After signing, publish the file's new SHA-256 hash with the release.
