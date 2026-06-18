# Codex Pets compatibility

WinPet reads Codex custom pets directly from the same directory:

```text
${CODEX_HOME:-$HOME/.codex}/pets/<pet-name>/
├── pet.json
└── spritesheet.webp
```

No conversion or WinPet-specific copy is required.

## Manifest

```json
{
  "id": "pet-name",
  "displayName": "Pet Name",
  "description": "One short sentence.",
  "spritesheetPath": "spritesheet.webp"
}
```

## Atlas contract

- PNG or WebP
- 1536 × 1872 pixels
- 8 columns × 9 rows
- each frame is 192 × 208 pixels
- transparent background and transparent unused cells

WinPet validates these dimensions before listing a pet.

## State mapping

| WinPet state | Codex animation row |
| --- | --- |
| working | running (row 7) |
| short idle | idle (row 0) |
| warning | waiting (row 6) |
| break due | failed (row 5) |
| resting | idle (row 0) |
| welcome back | waving (row 3) |
| paused | waiting (row 6) |

Frame counts and timing follow the Codex contract exactly.

Install or create community pets through Codex, refresh them there, and then
select the same pet from WinPet settings.
