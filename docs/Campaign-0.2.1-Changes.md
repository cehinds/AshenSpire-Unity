# Campaign 0.2.1

Second Wind now grants one energy without replacing itself. It trades hand space for energy, closing the free energy/draw cycling route. The new regression test exercises the actual command and checks energy, hand and draw pile. The model suites now total 164 checks, including 48 complete seeded runs.

Browser automation waits for a control layout emitted after the observed state change and for Play to become enabled after selection. Failed runs capture the live screen, state and control bounds. This addresses the publication-run failure where a slow runner reached Play before the selection was rendered; it does not bypass failures.

The UI-only scene now gets an owned AudioListener when no scene listener exists, so its procedural feedback has a listener. An owner-provided listener is preserved. The build version and bug-report preview are updated to 0.2.1.

Web export stamps the source digest into all four loader/runtime URLs, and the Pages Play link carries the same digest. Package validation rejects missing cache tokens. This prevents cached files from different checkpoints being mixed when following a newly published build link.

Web, Windows and Android were rebuilt. Current screenshots and playtest evidence are under Published/Screenshots. Initial four-class browser evidence and screenshots remain preserved under Published/History/0.2.0; the original 0.1 checkpoint is also preserved. Physical Android/iOS and graphical Windows validation remain outstanding.
