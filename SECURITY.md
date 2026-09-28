# Security and operational scope

LabSpace is an experimental visual-programming environment. Its bundled acquisition is simulated. It is not certified for physical equipment operation, safety-critical applications, medical use or hard-real-time control.

Imported project JSON is treated as data and is not evaluated as JavaScript, C#, native code or a plugin. The serializer and runtime enforce document size, object count, nesting, sample and execution limits. Browser storage is local to the origin; explicit export requires a user action. No application analytics or project upload is implemented. Normal dependency and hosting requests still occur when loading the application.

Do not put credentials or sensitive measurement data in public bug reports. Browser site data and downloaded project files may be accessible to other users or software with access to the same device. The application does not provide encryption at rest, multi-user authorization or a secure hardware-transport layer.

The test-only read-only diagnostic snapshot is opt-in via `?test=1`. It can expose current project labels and values to scripts on that page; do not enable it when embedding untrusted same-origin scripts. It is not a security boundary.

Report security issues through the repository owner's GitHub security reporting channel when available. Otherwise contact the maintainer privately before posting exploit details. No supported-production-version commitment is implied for this alpha release.
