# Maintaining player guides

Keep guides consistent with the implementation in the same change that changes gameplay.

## Readable layout

- Separate headings, paragraphs and numbered steps with blank lines in the source.
- Use plain ASCII double quotation marks (`"`) in player-facing text, not guillemets or curly quotes. In XML attributes, encode embedded quotation marks as `&quot;`.
- Put each numbered action on its own line using `1.`, `2.`, etc. The guide parser renders these as individual rows.
- Use short headings, equipment illustrations and highlighted boxes for the main workflow and important warnings.
- Check rendered heading controls and step rows with `RadiantGuidebookTest`; valid XML alone does not guarantee readable output.

## Shuttle saving

`ShipSaving.xml` is the player-facing reference opened by the question mark next to the shipyard's save button. Update it whenever eligibility, persistence filters, storage limits or deployment rules change. Keep `Storage.xml` as a short introduction linking to this page rather than duplicating the rules. Keep local file locations and account/character binding details out of the player guide, as requested by the server owner.

Review these sources when updating the guide:

- `Content.Server/_NF/Shipyard/Systems/ShipyardSystem.Saving.cs`: eligibility, docking, ID and deployment checks.
- `Content.Server/_Triad/Shipyard/ShipyardGridSaveSystem.cs`: included entities, contents, materials and save confirmation.
- `Content.Server/_HL/Shipyard/ShipSaveYamlSanitizer.cs`: removed entities and reset state.
- `Resources/Prototypes/_Triad/ship_save_limits.yml`: storage limits.
- `Content.Client/_Triad/Shipyard/Save/ShipFileManagementSystem.cs`: local files and backups.

Use the live percentage shown by the console instead of documenting a fixed deployment price. Run `RadiantGuidebookTest` after editing guide markup or navigation.
