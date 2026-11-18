# Collectible tables (#37)

The lower-screen map pins and the CHECKLIST page read a generated table of
where each collectible sits: room name, kind, save key and position in the
room. These scripts build that table from a local copy of each game.

```
python -m venv venv && venv/Scripts/pip install UnityPy TypeTreeGeneratorAPI
venv/Scripts/python -I extract_hk.py "<Hollow Knight>/hollow_knight_Data" hk.json
venv/Scripts/python -I extract_ss.py "<Silksong>/Hollow Knight Silksong_Data" ss.json [--all]
venv/Scripts/python -I emit_table.py hk.json ../hollow-knight-patches/src/lower/HkCollectibleData.cs HollowKnightPatches.Lower HkCollectibleData
venv/Scripts/python -I emit_table.py ss.json ../silksong-patches/src/dualscreen/SsCollectibleData.cs "" SsCollectibleData
```

Only generated room names, positions and save keys go into the repository,
never game data. Silksong's extractor reads the Linux build's addressable
scene bundles; `--all` also lists pickups whose item is not a tracked kind, for
reviewing the classification.

What is tracked:

- Hollow Knight: grubs, mask shards, vessel fragments, world charms, whispering roots.
- Silksong: mask shards, spool fragments, tools, memory lockets, relics, fleas.

Shop stock, quest rewards and items handed over by scripted events (Silksong
crests among them) have no pickup in a scene, so they are not on the map.
