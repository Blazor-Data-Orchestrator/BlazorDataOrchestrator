<img width="426" height="176" alt="BlazorDataOrchestratorLogo" src="https://github.com/user-attachments/assets/9df86186-193a-4a48-a2ba-e751abbf21eb" />

# Blazor Data Orchestrator

Welcome to the **Blazor Data Orchestrator** wiki — your comprehensive guide to installing, configuring, and operating the platform.

## What is Blazor Data Orchestrator?

Blazor Data Orchestrator is an open-source distributed job orchestration platform built on **.NET Aspire** and **Blazor Server**. It lets you write a C# or Python job directly in your browser, hit compile, and have it running on Azure in minutes.

The platform packages job code as NuGet packages and executes them through a queue-based agent architecture. Jobs can be triggered on a schedule, manually from the UI, or via webhook endpoints. Azure Storage provides the backbone for package storage (Blob), job queuing (Queue), and structured logging (Table), while Azure SQL stores all job configuration and metadata.

Key capabilities include an in-browser Monaco code editor with AI-assisted development, NuGet packaging for job distribution, multi-environment configuration, horizontal agent scaling across multiple queues, heartbeat-based reliability for long-running tasks, and a guided Install Wizard for first-time setup. The entire system deploys to Azure Container Apps with a single `azd up` command.

---

## The Problem

Every Azure developer eventually faces the same challenge: you need to run recurring data jobs — ETL pipelines, report generators, API integrations, cleanup scripts — but the options are either heavyweight platforms that demand weeks of setup, or fragile cron jobs held together with hope.

Azure Functions with Timer Triggers work for basic scenarios but break down when you need job grouping, parameterization, execution history, and horizontal scaling. Full platforms like Azure Data Factory or Apache Airflow carry significant operational overhead and learning curves that many teams cannot justify for internal automation workloads.

The result? Teams resort to fragile PowerShell scripts on VMs, unmonitored console apps, or over-engineered solutions that cost more to maintain than the problems they solve.

Blazor Data Orchestrator fills this gap — a **lightweight, self-hosted job platform** built on Azure services that developers already know. It is production-ready, open-source software you can clone and deploy the same day.

## Architecture Overview

<img width="800" height="600" alt="Blazor Data Orchestrator Architecture" src="https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/raw/main/wiki-content/images/BlazorDataOrchestratorArchitecture.png" />

## Quick Start

Go from `git clone` to a fully operational job automation system in minutes:

1. **Prerequisites** — Install [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Podman).
2. **Clone & restore** — `git clone https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator.git && cd BlazorDataOrchestrator && dotnet workload restore`
3. **Run locally** — `aspire run` — Aspire starts SQL Server, Azurite, and all application services automatically. Complete the Install Wizard on first launch.
4. **Deploy to Azure** — `azd up` — Provisions Azure SQL, Storage, Container Registry, and Container Apps, then builds and deploys all services in a single command.

See the [Installation](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Installation) guide for detailed local setup and the [Deployment](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Deployment) guide for Azure deployment.

## Navigation

* [Features](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Features) — Full feature catalogue
* [Requirements](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Requirements) — System prerequisites and infrastructure
* [Installation](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Installation) — Step-by-step setup guide
* [Operation](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Operation) — Day-to-day usage guide
* [Job Development](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Job-Development) — Overview of job development approaches
  - [Online](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Online) — Browser-based code editor
  - [Visual Studio](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Visual-Studio) — Local development with the Job Creator Template
* [Deployment](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Deployment) — Production deployment instructions
* [Frequently Asked Questions](https://github.com/Blazor-Data-Orchestrator/BlazorDataOrchestrator/wiki/Frequently-Asked-Questions) — Common Q&A and troubleshooting
