# Security Boundary

This repository contains only source-neutral MgDataKit core code.

Do not commit any of the following here:

- project settings, catalogs, or generated data assets;
- access tokens, application IDs, application secrets, or private service URLs;
- spreadsheets, table exports, story/localization data, or other project content;
- concrete data-source adapters or bundled third-party service clients.

Keep integrations in a separate repository or private project package. Core integrations should expose only the extension contracts defined in `Editor/Core/`.
