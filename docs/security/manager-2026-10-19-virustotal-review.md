# Viora Manager — VirusTotal investigation (19 October 2026)

**Status: Investigation open. NOT a confirmed false positive; NOT a malware finding independently verified by us.**
This report tracks the same publicly released Viora Manager v1.4.1 Windows EXE observed by the user in VirusTotal and compares packaging variants made from the *same Manager source*.

## Public baseline evidence

- Published release: https://github.com/balcony3232/Viora/releases/tag/manager-v1.4.0-preview
- Release asset: \`VioraManager-v1.4.1.exe\` (despite the older release tag name).
- Published asset SHA-256: \`2f53b7d633adafcda7bbb4c512fc0d83adeff83e71d27c1b784640c0cb972c29\`.
- VirusTotal file record (same SHA-256): https://www.virustotal.com/gui/file/2f53b7d633adafcda7bbb4c512fc0d83adeff83e71d27c1b784640c0cb972c29
- VirusTotal screenshot: **1/69** static engines detected the file. The one vendor was **Zillya**, label \`Trojan.AgentAgen.Win32.210177\`. The Yomi Hunter sandbox independently marked its analysis MALWARE based on heuristic observations; that is not negated by the 1/69 static tally.
- Screenshots of Yomi behavior highlighted anti-debugging/dynamic API heuristics, \`SetUnhandledExceptionFilter\`, \`LdrGetProcedureAddress\`, process/registry observations, and temporary DLL execution. These require further validation. The screenshot showed zero executed commands and services, which is only a *partial observation*, not proof that malicious activity is absent.
- In the provided Network Communication view, three GET requests used Microsoft \`crl.microsoft.com\` certificate revocation URLs with HTTP 200 results. UDP port 53 DNS and Microsoft \`aka.ms/dotnet\` strings occurred in the report; memory-matched URL strings are **not proof of network connections**. Other behavior could have escaped sandbox observation.
- The inspected source contains Windows APIs for enumerating / arranging existing Roblox Player windows and a disabled-by-default experimental named mutex. This provides possible benign explanations for some heuristic matches, but is not a substitute for runtime analysis.

## Controlled diagnostic builds

A dedicated build on branch \`feature/manager-antivirus-diagnostics\` creates:
1. **A — compressed single-file:** Release win-x64 self-contained .NET 8 Manager build equivalent to the original packaging flags.
2. **B — uncompressed single-file:** Identical source, dependencies, configuration, symbols and target but \`EnableCompressionInSingleFile=false\`.
3. **C — multi-file:** Same source and .NET 8 self-contained publishing without single-file bundling. **All files are required to run**; the EXE inside the ZIP is not a standalone application.

Build workflow: [manager-antivirus-diagnostics.yml](../../.github/workflows/manager-antivirus-diagnostics.yml).

**Completed diagnostic build:** [GitHub Actions #39303155336](https://github.com/balcony3232/Viora/actions/runs/39303155336), successful, source commit `3dae5d4de008200f393007b54984ca1e5a2d0804`. The job log reports **.NET SDK 10.0.401 targeting net8.0-windows**. This SDK/runtime distinction matters when comparing to older builds; the three new variants were compiled in the same job with the same SDK.

GitHub Actions artifacts (download from that run's **Artifacts** section):
- `manager-diagnostic-A-compressed-singlefile` — SHA-256 `c22e1de1ecc51c03c86ce6cf0306effaf7ec690a134d1298cd95e734e7a3f77a` (71,686,419 bytes)
- `manager-diagnostic-B-uncompressed-singlefile` — SHA-256 `7f5c52e5f621ec64d01be89efab4ea96ad077e6f988deaaf134f37a78715d9fd` (161,770,520 bytes)
- `manager-diagnostic-C-multifile` ZIP — SHA-256 `554dd58c186091c137160af0e475c2df2681e659c3dea89647f02fb1c52232c9` (72,202,941 bytes); the **inner EXE** SHA-256 is `219e6382cbf47baa05bda6323d501438e5a547e034211872a6e2f3f2b67683b1` (190,464 bytes) and requires adjacent DLL/runtime files.

**These are unscanned research artifacts:** the GitHub workflow verified build success and hashes, **not that any of them are malware-free**.

These variants are **research artifacts only**, not public stable releases. Rebuilding the compressed control can change hashes independently of source because of SDK or build environment changes, so do not assume new A is bitwise identical to the original released file. Inspect \`BUILD-MANIFEST.txt\` for the exact source commit, SDK and SHA-256 of each sample.

### Comparison protocol

1. Download each artifact from **the same completed diagnostic workflow run** and verify \`SHA256\` hashes against the included manifest.
2. Scan **A's EXE** on VirusTotal, then **B's EXE**. Scan **C's inner EXE** for static comparison, but also analyze **C's extracted multi-file folder in a disposable isolated Windows VM** if testing behavior. A lone multi-file EXE will not behave like the complete app.
3. Record for each sample: SHA-256, VirusTotal report URL, analysis date, static detections/vendor names, individual sandbox verdicts, **specific** API/process/registry/file/network evidence and whether the program successfully executed in the sandbox.
4. Compare the *same scanner engines, versions, and analysis conditions* where possible. Scanner reputations and engines can change, and upload processing is asynchronous. Detection-count differences alone do not establish the cause.
5. If packaging materially changes verdicts and benign functionality is verified, submit the baseline and comparisons to the detecting vendor for false-positive review. Do **not** advise users to disable AV or suppress SmartScreen. Until concluded, encourage independent verification and avoid claiming complete safety.

**VirusTotal privacy notice:** VirusTotal submissions and results may be shared with security partners/researchers. Upload only publicly distributable binaries that contain no secrets, tokens, credentials, personal files or private customer data. These particular Manager builds should be inspected to confirm that before submission.

## Preliminary A/B VirusTotal observations (19 October 2026)

Screenshots supplied while both analyses were **still running** show that:
- **A (compressed single-file)**, SHA-256 `c22e1de1ecc51c03c86ce6cf0306effaf7ec690a134d1298cd95e734e7a3f77a`: Zillya `Trojan.AgentAgen.Win32.210177` at **1/68** engines so far (about 141.5 seconds elapsed).
- **B (uncompressed single-file)**, SHA-256 `7f5c52e5f621ec64d01be89efab4ea96ad077e6f988deaaf134f37a78715d9fd`: **the same Zillya `Trojan.AgentAgen.Win32.210177` label** at **1/30** engines so far (about 78.9 seconds elapsed).
- The screenshot hashes matched the diagnostic build manifest. Engine counts in these images are **not final**; no full sandbox report for A or B was supplied yet.
- **Interpretation:** Disabling single-file compression did **not** remove the observed Zillya detection, so single-file compression alone is unlikely to explain it. This is **not proof** of harmlessness or maliciousness. A final A/B scan comparison, multi-file control, and detailed behavior evidence are outstanding.

## Observations to fill in after the user scans

| Sample | SHA-256 | VT analysis link | Static engines | Sandbox conclusions | Other evidence |
|---|---|---|---|---|---|
| Released v1.4.1 | \`2f53b7d6...c972c29\` | [Original report](https://www.virustotal.com/gui/file/2f53b7d633adafcda7bbb4c512fc0d83adeff83e71d27c1b784640c0cb972c29) | 1/69 (Zillya) | Yomi Hunter: MALWARE (heuristic) | Microsoft CRL traffic seen |
| A — compressed control | `c22e1de1...e7a3f77a` | Pending | **1/68 provisional; Zillya AgentAgen** | Pending | Analysis was still running |
| B — no single-file compression | `7f5c52e5...8715d9fd` | Pending | **1/30 provisional; Zillya AgentAgen** | Pending | Analysis was still running |
| C — multi-file (inner EXE) | `219e6382...2f3f2b1` | Pending | Pending | Pending | Built successfully; requires complete folder to run |

## Assessment

The information currently supports **multiple hypotheses**: heuristic false positive from packaging, a benign Windows/.NET function misclassified, or a genuine issue not yet identified. The available screenshots do not permit a definitive malware/not-malware conclusion. Further dynamic and source-to-binary analysis may be needed.

## Release handling

The currently published v1.4.1 binary is **unchanged** by the diagnostic branch. This document/issue will be updated with observed, verifiable results before any false-positive claim or distribution decision. If new evidence suggests actual compromise, recommend pausing distribution and investigating the build pipeline and published binary.
