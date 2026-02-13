# Wiki Content

This directory contains the content for the [Blazor Data Orchestrator Wiki](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki).

## Purpose

The files in this directory are synchronized to the GitHub wiki through the `update-wiki.yml` GitHub Action workflow. This allows wiki content to be version-controlled alongside the main codebase.

## Automated Sync

Changes to files in this directory will automatically be pushed to the wiki when:
1. Changes are merged to the `main` branch
2. The `Update Wiki` workflow is manually triggered

## Wiki Pages

The following stub pages have been created:

### Main Pages
- **Features.md** - Describes the features of Blazor Data Orchestrator
- **Requirements.md** - System requirements
- **Installation.md** - Installation instructions
- **Operation.md** - How to operate the application
- **Deployment.md** - Deployment instructions
- **Frequently-Asked-Questions.md** - FAQ page

### Job Development
- **Job-Development.md** - Parent page for job development
- **Online.md** - Online job development guide
- **Visual-Studio.md** - Visual Studio development guide (with screenshot)

### Navigation
- **Home.md** - Main wiki page with navigation structure

## Editing Wiki Pages

To edit wiki pages:
1. Edit the corresponding `.md` file in this directory
2. Commit and push your changes
3. The GitHub Action will automatically sync the changes to the wiki

## Manual Sync

To manually sync the wiki content:
1. Go to Actions tab
2. Select "Update Wiki" workflow
3. Click "Run workflow"
