# MdictViewer documentation

All four applications use the following document responsibilities. Application-specific formats and architecture remain separate reference pages.

| Document | Responsibility |
| --- | --- |
| [Getting Started](Getting-Started.md) | Runtime requirements, launch, and first use |
| [Application Overview](Application-Overview.md) | Purpose, modules, and application-specific architecture |
| [Configuration](Configuration.md) | Settings location, ownership, preservation, and recovery |
| [Testing](Testing.md) | Core/UI/Full entry points, media preparation, and published acceptance |
| [Publishing](Publishing.md) | Portable profile, staged updates, and GitHub Release workflow |
| [Release Notes](Release-Notes.md) | Distribution changes and runtime requirements |
| [Health Check](Health-Check-2026-10-13.md) | Dated verification evidence and limitations |

## Application-specific references

- [Architecture](architecture.md)
- [Dictionary resources](local-resources.md)
- [Single-file implementation](single-file-deployment.md)

## Maintenance rules

Keep executable commands in Testing and Publishing; link them from usage guides. Keep settings rules in Configuration. Record dated results in Health Check rather than implying that an old result verifies later changes. Domain references describe file formats and behavior, not another publishing pipeline. Historical audits and plans remain historical and are not the current workflow entry point.
本轮代码结构审查与整改见 [Code Review](Code-Review-2026-10-13.md)。
