# Custom AI Virtual Agent -- Architecture Overview

## Purpose

This document describes the **high-level architecture** for building a
**custom Copilot-like virtual agent** using: - Custom Angular chat UI -
Orchestrator API on Azure App Service - Microsoft Foundry (Azure AI
Foundry) as the AI agent runtime - Internal enterprise APIs backed by
Databricks

The goal is to **accelerate development**, reduce custom AI plumbing,
and maintain **enterprise-grade security, governance, and
observability**, while retaining full control over the user experience.

------------------------------------------------------------------------

## Diagram-style Architecture (Recommended)

    flowchart LR
    U[Users<br/>(Web Browser)] -->|Entra ID Login| UI[Angular Chat UI<br/><small>Custom UX</small>]

    UI -->|Chat / Stream / Feedback| ORCH[Chat Orchestrator API<br/><small>Azure App Service</small>]

    ORCH -->|Invoke Agent| FOUNDRY[Microsoft Foundry<br/><small>Agent Service</small>]

    FOUNDRY -->|OpenAPI Tool Calls<br/>Managed Identity| API[Internal Knowledge APIs<br/><small>Azure App Service</small>]

    API -->|Queries / Jobs| DB[Databricks<br/><small>Lakehouse & SQL Warehouse</small>]

    %% Cross-cutting concerns
    ORCH -.-> OBS[Application Insights<br/><small>Tracing & Metrics</small>]
    FOUNDRY -.-> OBS

    UI -->|Thumbs / Forms| FB[Feedback Store]
    ORCH --> FB

------------------------------------------------------------------------

## Core Components

### Angular Chat UI

Owns the user experience: chat streaming, feedback controls, quick
actions, and forms. Authentication is handled using **Microsoft Entra ID
(MSAL)**.

### Chat Orchestrator API

Acts as the enterprise control plane: - Authentication & authorization -
Conversation/session management - Policy enforcement - Calls Foundry
Agent endpoints - Persists transcripts and feedback

### Microsoft Foundry

Provides the AI backbone: - Agent instructions & reasoning - Model
deployment - Tool orchestration - Safety, evaluation, and tracing

### Internal APIs & Databricks

Internal APIs expose structured business data and actions. Databricks is
accessed only through these APIs, never directly by the agent.

------------------------------------------------------------------------

## End-to-End Flow

1.  User authenticates and sends a message
2.  Orchestrator validates and forwards to Foundry
3.  Agent calls internal APIs as tools
4.  APIs query Databricks and return facts
5.  Agent responds with text + UI actions
6.  UI renders response and captures feedback

------------------------------------------------------------------------

## Key Benefits

-   Faster development using Foundry agent runtime
-   Copilot-like UX with custom UI control
-   Strong security and governance
-   End-to-end observability
-   Easy iteration and scaling

------------------------------------------------------------------------

## Intended Audience

Engineering leadership, architects, platform teams, and developers.
