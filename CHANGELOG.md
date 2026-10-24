# Changelog

## 0.1.0

- Add LC Office elevator and storage cleanup when the ship leaves.
- Add fallback cleanup before the next level loads.
- Disable leftover collision, player physics regions, navigation obstacles and navigation surfaces.
- Unregister retired elevator floor links from PathfindingLib.
- Clear player references to retired elevator physics transforms.
- Guard storage updates against destroyed targets.
- Preserve server authority when despawning network objects.
