## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to refresh the graph (AST-only, no API cost), then run `graphify export obsidian` and `graphify export wiki` to regenerate the vault and wiki, which do not update themselves. The git post-commit hook refreshes only `graphify-out/graph.json`, so the vault and wiki go stale until those two export commands are re-run.
- The Obsidian vault at `graphify-out/obsidian/` contains 7,442 Markdown notes plus `graph.canvas`. Each note has YAML frontmatter (`source_file`, `type`, `community`, `location`, `tags`) and a `## Connections` list of `[[wikilinks]]`. It is the human browsing/visualisation layer, opened in the Obsidian desktop app. Agents should still use `graphify query`, `graphify path`, and `graphify explain` for codebase questions; the vault is not a substitute, and reading its notes directly is the expensive path the graph exists to avoid.
- The wiki at `graphify-out/wiki/` contains 417 articles, with `graphify-out/wiki/index.md` as the agent entry point.
- `graphify-out/` is gitignored, so the vault and wiki are local derived artifacts and are never committed.
