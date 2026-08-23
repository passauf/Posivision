[README.md](https://github.com/user-attachments/files/31341238/README.md)
# Posivision (working title)

Camera-only physiotherapy serious game. **Unity 2022 LTS**, **C#**, on-device **MediaPipe Pose**. No wearable sensors.

This repo contains **application scripts only**. It is not a runnable Unity project: scenes, art, `Library/`, and the MediaPipe Unity plugin are omitted on purpose so reviewers can read architecture without a multi-gigabyte dump.

## Stack

- Real-time pose (33 landmarks), visibility gating, One Euro smoothing
- Shoulder-width–normalized **XY** range of motion (camera depth is not used for clinical angles)
- Inference off the Unity main thread; zero-allocation hot path targeting 30+ FPS
- Compensation / form-quality heuristics, assisted vs independent rep counting, FastDTW
- P-controller dynamic difficulty on adaptive ROM targets
- Local clinician reports: HTML + CSV, AES-encrypted at rest (`.enc`), clinician PIN to open — no cloud upload of patient metrics

Decision support only. Not a diagnostic device claim.

## Layout (`Assets/Scripts`)

| Folder | Responsibility |
| --- | --- |
| `Analysis/` | Pose scale, facing/side gates, DTW, quality, assisted reps, session closeout |
| `Analysis/Movement/` | Per-exercise analyzers and rep policies (`IMovementAnalyzer`, shoulder flexion) |
| `Jobs/` | Job System / Burst joint-angle work |
| `Avatar/` | Mannequin, ROM arcs, face-strain features (no session video stored) |
| `CarePlan/` | Local care state, AES patient vault, clinician PIN, report HTML |
| `Coaching/` | Voice coach and target advisor |
| `Exercise/` | Movement catalog |
| `UI/` | Menus, pre-session setup, exercise HUD, history, session compare |
| `Privacy/` | Consent / notice copy |
| `Localization/` | Turkish and English UI strings |
| `Debug/` | Editor / Android perf HUD |
| *(root)* | `PhysioAnalyzer.cs` (orchestration), session/report/export, patient profile |

**Read first:** `PhysioAnalyzer.cs` → `Analysis/Movement/IMovementAnalyzer.cs` → `CarePlan/PatientVault.cs`

## Demo media (`docs/`)

Self-demo only (author or consenting volunteer). No real patient names, IDs, or clinic records. The product does **not** persist session video; the clip below is a one-off screen recording for GitHub.

| File | What you are looking at |
| --- | --- |
| `MainMenu_Screenshot` | Main menu: local profiles, start session, clinician/progress entry. Data stays on device. |
| `TargetAngle_Screenshot` | Pre-session target panel: planned ROM (degrees) and rep count before the camera loop starts. |
| `InSession_Screen_Record` | ~60s in-session capture: webcam + pose overlay / HUD (reps, form cues). Not a stored clinical video file. |
| `HTML_SessionReport_Screenshot` | Post-session clinician HTML report: ROM curve, quality / compensation summary, encrypted at rest in the app. |
| `Progress Report — Kadir Özdemir` | Multi-session progress export (print/PDF of the local HTML report). Decision support, not a diagnosis. |

```markdown
![Main menu](docs/main-menu.png)
![Target angle panel](docs/target-angle-panel.png)
![Session report](docs/session-report.png)
```

## Not in this repo

MediaPipe/OpenCV natives and vendor samples, Unity scenes/prefabs, PIN secrets, or **real** patient folders. Demo screenshots/PDF in `docs/` are redacted self-captures.

## License

All rights reserved unless a license file is added. MediaPipe and other third-party plugins keep their own licenses; they are not part of this tree.
