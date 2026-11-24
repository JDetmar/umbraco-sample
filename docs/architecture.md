# Architecture

How the deployed site fits together in Azure, per environment. This is the living overview: update it in the same pull request as any change to `infra/` that adds, removes or reconnects a resource. The decisions behind it are in [ADR 0004](adr/ADR-0004-azure-container-apps-hosting.md) and [ADR 0010](adr/ADR-0010-azure-environment-design.md). The trust boundaries for the lead form are in [threat model 0001](threat-models/0001-lead-form.md).

## Runtime view (one environment)

Solid lines are declared in `infra/UmbracoSample.Infra` today. Dashed elements are planned but not declared yet.

```mermaid
flowchart LR
    visitor([Visitor browser])
    editor([Editor browser])

    subgraph edge["Edge (planned, Phase 5)"]
        fd["Front Door Standard<br/>WAF, HSTS, cache"]
    end

    subgraph rg["Resource group per environment (dev, prod)"]
        subgraph vnet["Virtual network"]
            subgraph cae["Apps subnet: Container Apps environment<br/>peer-to-peer TLS"]
                app["Container App: site<br/>Umbraco 17 on .NET 10<br/>image pinned by digest"]
            end
        end
        id["User-assigned<br/>managed identity"]
        sql[("Azure SQL serverless<br/>Entra-only auth")]
        blob[("Storage: private<br/>media container")]
        kv["Key Vault<br/>RBAC"]
        ai["Application Insights<br/>+ Log Analytics"]
        budget["Consumption budget<br/>80% / 100% alerts"]
    end

    map["Marketing automation<br/>platform API (planned, D4)"]

    visitor -. HTTPS .-> fd
    editor -. HTTPS .-> fd
    fd -. "HTTPS to ingress,<br/>TLS to container" .-> app
    app -- runs as --> id
    cae -- "Microsoft.Sql service endpoint;<br/>SQL VNet rule admits only this subnet" --> sql
    id -- "Entra token" --> sql
    id -- "Blob Data Contributor" --> blob
    id -- "Secrets User" --> kv
    kv -- "admin password, imaging key<br/>(Key Vault references)" --> app
    id -- "Metrics Publisher" --> ai
    app -. "HTTPS, lead form" .-> map

    classDef planned stroke-dasharray: 5 5
    class fd,map planned
```

Until the edge exists, the Container App's own HTTPS ingress is the entry point, and forms stay off on any public environment (threat model 0001, R2).

## Delivery view

```mermaid
flowchart LR
    dev([Contributor or agent]) -- pull request --> gh["GitHub: main<br/>rulesets require CI"]
    gh -- "CI: validate, integration,<br/>image, e2e, CodeQL" --> img["Container image<br/>by digest, with SBOM"]
    gh -- "infra workflow:<br/>mock tests, preview" --> pulumi["Pulumi Cloud<br/>stack state"]
    owner([Owner]) -- "pulumi up<br/>(owner only)" --> pulumi
    pulumi -- declares --> azure["Azure resource group"]
    img -. "digest set in stack config" .-> azure
```

No agent runs `pulumi up` or deploys (CLAUDE.md). Locally, `make run` starts the same site with SQL Server in containers through Aspire (ADR 0002, ADR 0012), with no Azure resources.
