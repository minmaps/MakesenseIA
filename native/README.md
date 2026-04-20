# Makesense Native

Base desktop native pour la réécriture Windows haute performance.

## Contenu

- `Makesense.Desktop`: UI `WPF/MVVM`
- `Makesense.Formats`: contrats de données et sérialisation
- `Makesense.Core`: DLL `C++20` avec `D3D11 + Direct2D`, budgets RAM/VRAM, pools de threads et C API

## Build

```powershell
pwsh ./native/build-native.ps1 -Configuration Debug
```

Le script :

1. configure et compile `Makesense.Core` avec CMake
2. copie la DLL native dans `native/artifacts/core/<Configuration>`
3. compile la solution `.NET`

## APIs natives exposées

- `ms_create_engine`
- `ms_set_performance_limits`
- `ms_open_project`
- `ms_open_images`
- `ms_set_active_image`
- `ms_handle_input_event`
- `ms_render`
- `ms_run_inference`
- `ms_export_annotations`
- `ms_shutdown`

## État actuel

La base native est opérationnelle pour :

- héberger une surface Win32 dans WPF via `HwndHost`
- rendre un HUD Direct2D sur swap chain D3D11
- appliquer des plafonds RAM/VRAM
- piloter des pools I/O, decode et inference
- enregistrer/charger un manifeste JSON de session

Les intégrations effectives `CUDA/TensorRT` et la parité complète de l’éditeur historique demandent encore du travail additionnel et des SDK natifs non embarqués dans ce dépôt.
