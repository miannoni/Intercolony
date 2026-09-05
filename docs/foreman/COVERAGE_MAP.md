# Coverage Map

This map is bidirectional. Plan Requirement identifiers are the stable `F01`–`F25` identifiers in
the authorized Plan. All Requirements are open; none currently carries a Disposition.

| Plan Requirement | Intent summary | Covering Slices | Accounting |
|---|---|---|---|
| `F01` | Quiet successful auto-ready; actionable failures | `S01` | open; no Disposition assigned |
| `F02` | Cancel terminates Produce loop | `S04` | open; no Disposition assigned |
| `F03` | Area Produce/Resume/Pause/Stop semantics | `S05`, `S06`, `S07` | open; no Disposition assigned |
| `F04` | Programmable Produce modes, constraints and workers | `S08`, `S09`, `S10`, `S11` | open; no Disposition assigned |
| `F05` | Filter-compatible receiving locations and fallback | `S12`, `S13` | open; no Disposition assigned |
| `F06` | Optional employee apparel policies | `S22` | open; no Disposition assigned |
| `F07` | Commitment versus actual completed production | `S30` | open; no Disposition assigned |
| `F08` | Limited commercial-reputation goodwill | `S29` | open; no Disposition assigned |
| `F09` | Employment happiness goodwill | `S23` | open; no Disposition assigned |
| `F10` | Procurement relationship progression | `S28` | open; no Disposition assigned |
| `F11` | Progressive distance-biased RFQ responses | `S14` | open; no Disposition assigned |
| `F12` | Preprogrammed recurring agreement caravans | `S18`, `S19`, `S20`, `S21` | open; no Disposition assigned |
| `F13` | Auto-renew visible on employee cards | `S24` | open; no Disposition assigned |
| `F14` | Collapsible contracts | `S26` | open; no Disposition assigned |
| `F15` | Agreements default Auto-ready ON | `S02`, `S03` | open; no Disposition assigned |
| `F16` | Employee card emphasizes primary metrics | `S24` | open; no Disposition assigned |
| `F17` | Secondary employee actions move behind compact menu | `S25` | open; no Disposition assigned |
| `F18` | Procurement unit price visible | `S27` | open; no Disposition assigned |
| `F19` | Material replacement-cost estimate | `S31` | open; no Disposition assigned |
| `F20` | Recent attributable direct labor cost | `S32` | open; no Disposition assigned |
| `F21` | Economically meaningful settlement logistics | `S15`, `S16`, `S17` | open; no Disposition assigned |
| `F22` | Player colonists supplied to labor market | `S37` | open; no Disposition assigned |
| `F23` | Requested equipment and refundable bond | `S34`, `S35` | open; no Disposition assigned |
| `F24` | Emergency/urgent hiring | `S36` | open; no Disposition assigned |
| `F25` | Labor RFQs with market-quoted wages | `S33` | open; no Disposition assigned |

## Slice-to-Requirement index

| Slices | Source Plan Requirement(s) |
|---|---|
| `S01` | `F01` |
| `S02`, `S03` | `F15` |
| `S04` | `F02` |
| `S05`, `S06`, `S07` | `F03` |
| `S08`, `S09`, `S10`, `S11` | `F04` |
| `S12`, `S13` | `F05` |
| `S14` | `F11` |
| `S15`, `S16`, `S17` | `F21` |
| `S18`, `S19`, `S20`, `S21` | `F12` |
| `S22` | `F06` |
| `S23` | `F09` |
| `S24` | `F13`, `F16` |
| `S25` | `F17` |
| `S26` | `F14` |
| `S27` | `F18` |
| `S28` | `F10` |
| `S29` | `F08` |
| `S30` | `F07` |
| `S31` | `F19` |
| `S32` | `F20` |
| `S33` | `F25` |
| `S34`, `S35` | `F23` |
| `S36` | `F24` |
| `S37` | `F22` |

## Explicit Exclusions

- Merging to another branch, Workshop publication, release creation, destructive history changes,
  security/credential changes, and irreversible external actions are outside this Plan.
- Recreating every vanilla bill field is excluded by `F04`; only controls useful to repeated and
  contract-driven production are authorized.
- Full remote-settlement simulation for outside employment is excluded by `F22`; a reliable abstract
  outcome is authorized.
- Constant expensive full-map/pawn polling for labor costing is prohibited by `F20`.

