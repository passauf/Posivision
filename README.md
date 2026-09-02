# Posivision (working title)

Camera-only physiotherapy serious game. **Unity 2022 LTS**, **C#**, on-device **MediaPipe Pose**. No wearable sensors.

This repo contains **application scripts only**. It is not a runnable Unity project: scenes, art, `Library/`, and the MediaPipe Unity plugin are omitted on purpose so reviewers can read architecture without a multi-gigabyte dump.

## Stack

- Real-time pose (33 landmarks), visibility gating, One Euro smoothing
- Shoulder-width / torso-length–normalized **XY** range of motion (camera depth is not used for clinical angles)
- Inference off the Unity main thread; zero-allocation hot path targeting 30+ FPS
- Compensation / form-quality heuristics, assisted vs independent rep counting, FastDTW
- P-controller dynamic difficulty on adaptive ROM targets
- Local clinician reports: HTML + CSV, AES-encrypted at rest (`.enc`), clinician PIN to open — no cloud upload of patient metrics

Decision support only. Not a diagnostic device claim.

## Live movements

| Movement | Camera protocol | Analyzer |
| --- | --- | --- |
| Shoulder flexion | Side profile (working-arm side) | `ShoulderFlexion/` |
| Shoulder abduction | Frontal | `ShoulderAbduction/` |

Other exercises are catalogued in `ExerciseCatalog.cs` but not yet wired for live ROM.

## Layout (`Assets/Scripts` → `Scripts/`)

| Folder | Responsibility |
| --- | --- |
| `Analysis/` | Pose scale, facing/side gates, DTW, quality, assisted reps, session closeout |
| `Analysis/Movement/Common/` | Shared shoulder-elevation core, rep policy base, `MovementAnalyzerFactory` |
| `Analysis/Movement/ShoulderFlexion/` | Side-profile flexion ROM + rep policy (no foreshortening guards) |
| `Analysis/Movement/ShoulderAbduction/` | Frontal abduction ROM, foreground guards, yaw correction, rep policy |
| `Jobs/` | Job System / Burst joint-angle work |
| `Avatar/` | Mannequin, side-orbit camera, ROM arcs, pre-session hologram, face-strain features |
| `CarePlan/` | Local care state, AES patient vault, clinician PIN, report HTML |
| `Coaching/` | Voice coach and target advisor |
| `Exercise/` | Movement catalog and camera protocol metadata |
| `UI/` | Menus, pre-session setup, exercise HUD, history, session compare, `UiTheme` tokens |
| `Privacy/` | Consent / notice copy |
| `Localization/` | Turkish and English UI strings |
| `Debug/` | Editor / Android perf HUD |
| *(root)* | `PhysioAnalyzer` partials, session/report/export, patient profile |

### `PhysioAnalyzer` (partial orchestrator)

| File | Role |
| --- | --- |
| `PhysioAnalyzer.cs` | Inspector fields, exercise selection, composition root |
| `PhysioAnalyzer.Session.cs` | Begin/finish session, targets, avatar orbit, closeout |
| `PhysioAnalyzer.PosePipeline.cs` | Landmark queue, Job System, per-frame analysis |
| `PhysioAnalyzer.RepCoordinator.cs` | `Update` loop, rep counting, ROM assessment |

**Read first:** `PhysioAnalyzer.cs` → `Analysis/Movement/Common/MovementAnalyzerFactory.cs` → `CarePlan/PatientVault.cs`

## Demo media (repo root)

Self-demo only (author or consenting volunteer). No real patient names, IDs, or clinic records in code. The product does **not** persist session video; the clip below is a one-off screen recording for GitHub.

| File | What you are looking at |
| --- | --- |
| `MainMenu_Screenshot.png` | Main menu: local profiles, start session, clinician/progress entry. Data stays on device. |
| `TargetAngle_Screenshot.png` | Pre-session target panel: planned ROM (degrees) and rep count before the camera loop starts. |
| `InSession_Screen_Record.mp4` | ~60s in-session capture: webcam + pose overlay / HUD (reps, form cues). Not a stored clinical video file. |
| `HTML_SessionReport_Screenshot.pdf` | Post-session clinician HTML report: ROM curve, quality / compensation summary, encrypted at rest in the app. |
| `Progress Report - Kadir Özdemir.pdf` | Multi-session progress export (print/PDF of the local HTML report). Decision support, not a diagnosis. |

## Not in this repo

MediaPipe/OpenCV natives and vendor samples, Unity scenes/prefabs, `.meta` files, PIN secrets, or **real** patient folders. Demo screenshots/PDF at repo root are redacted self-captures.

## License

All rights reserved unless a license file is added. MediaPipe and other third-party plugins keep their own licenses; they are not part of this tree.
