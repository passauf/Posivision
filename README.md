# Posivision
[README.md](https://github.com/user-attachments/files/31341207/README.md)
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

## Not in this repo

MediaPipe/OpenCV natives and vendor samples, Unity scenes/prefabs, PIN secrets, patient folders, or generated HTML/CSV/Excel files.

## License

All rights reserved unless a license file is added. MediaPipe and other third-party plugins keep their own licenses; they are not part of this tree.
