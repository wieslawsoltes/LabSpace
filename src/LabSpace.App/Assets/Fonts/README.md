# Reproducible font assets

Run `python3 scripts/fetch-assets.py`. The application uses the OFL-licensed Carlito font fetched from the public google/fonts repository. Its immutable Git blob hash is checked before use. `OFL.txt` is bundled alongside the font. Font binaries are build assets, not source-control files. No proprietary NI or system fonts are copied.
