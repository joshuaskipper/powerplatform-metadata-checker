# PowerPlatform Metadata Checker
PowerPlatform Metadata Checker allows Power Platform Admins of Dataverse enabled environments to perform a check to validate if a solution's objects contain proper governance and best practices like no null descriptions and solution segmentation.
Audits Dataverse solutions to check governance and best practices such as no empty descriptions on tables, columns, and apps, plus solution segmentation and other component hygiene.
### Planned Evolution
- Phase 1 (Current): Local console/CLI tool authenticating directly to Dataverse for fast iteration on audit rules and report formatting.
- Phase 2 (Planned): API and CLI workflow accepting a short-lived Dataverse access token rather than long-lived client secrets.
