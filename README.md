# Handshake Desktop

Windows desktop port of the Handshake Android trade-show contact notebook.

## Current scaffold

The first increment establishes a WPF/.NET 8 desktop shell with local image selection (up to four pages per capture). Google Photos Picker, SQLite persistence, image normalization, OpenAI extraction, review, research, memo analysis, and export will be added incrementally.

## Build

Open `Handshake.Desktop.sln` in Visual Studio and run the project. Select the **.NET desktop development** workload in the Visual Studio Installer.

## Planned providers

- Local files and drag-and-drop
- Google Photos Picker API using explicit user selection
- Optional folder/watch-folder import later
