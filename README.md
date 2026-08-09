# MgDataKit Core

MgDataKit Core is a Unity data-asset and editor integration layer. It is intentionally independent from any game's concrete table definitions: game projects keep their table classes and source adapters outside this repository.

## Layout

- `Runtime/` — `MgDataBase`, attributes, merge helpers, and shared value types. Source metadata is editor-only and owned by the project Catalog.
- `Editor/` — catalog, import pipeline, validation, settings, tags, and editor windows.
- `Editor/Import/` — source-neutral grid mapping, import orchestration, and local cache utilities. No concrete data-source reader is included.

This repository contains only reusable core code. Project-specific assets, credentials, external services, third-party data readers, and game-specific adapters are intentionally excluded.

## Installation

Copy the contents of this repository into `Assets/MgDataKit` in a Unity 2022.3 project. Create the project settings and catalog assets from the MgDataKit editor menus after import.

Project assets use the current field layout directly; MgDataKit does not keep schema-version fields or automatic structure-migration paths. Each Unity project should create its own settings and catalog assets.

## Extension points

Optional integrations should implement `MgDataKit.Editor.IMgDataSourceAdapter` and/or `IMgDataSourceImporter` to convert an external source into the common grid. `IMgDataImportExtension` handles table-specific import behavior, and `IMgDataSyncOrderProvider` can provide deterministic cross-table synchronization order. MgDataKit discovers these implementations from loaded editor assemblies, so integrations stay outside the core repository.

EditorWindow integrations should implement `IMgDataKitEditorExtension`. The extension registers actions and empty-state views through `IMgDataKitEditorRegistry` and can provide `IMgDataKitAssetRowExtension` implementations for source-specific row UI. Functional slots currently include the left action bar, selected-type action bar, empty-state actions/views, Asset row source/actions, and window lifecycle callbacks. The type heading, type search/filter area, type list, and Asset heading intentionally remain core-owned until their extension contracts are stabilized.

Data sources may additionally implement `IMgDataSourceAdapter`. Each adapter owns a stable `SourceId`, binding validation, source reads, binding UI, source opening, and new-binding initialization. Adapters can optionally implement `IMgDataSourceBatchImportAdapter` for source-specific batch creation workflows. Catalog entries carry a generic `SourceId` and opaque source data without retaining source-specific compatibility fields.

Keep source adapters isolated from the core pipeline. An adapter should convert its source into the common grid representation, while table-specific reconciliation belongs in an import extension. This preserves a small stable API for future open-source contributors.

Source adapters can be maintained in separate repositories and discovered through the extension points above without changing the core importer.
