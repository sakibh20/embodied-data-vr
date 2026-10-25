# Floor grid

> Last updated: 2026-10-25 (M68). Already set up in `Experiment.unity`: the `Grid`
> object has a `GridGenerator`, wired into `GraphManager`'s Grid Generator slot.

## What it draws

Built by `GraphManager` every time a graph is generated, as children of the graph root,
so it lies on the floor with the graph and scales with it:

- **One cross line per data point**, across the value (sideways) axis, spanning the
  data's own value range plus `laneHalfWidth` of padding at each end (1 local unit, ≈0.2 m on the default 3 m walk)
  (M68: previously it ran from value 0 to the maximum, which put lines far outside the
  area for data far from 0). These mark where each point is along the walk.
- **A baseline** along the walk at value 0.

The grid uses the same value scale as the graph (including the sideways fit to the area
width, M68), so the cross lines always line up with the dots. It stays visible during
Retrace as a pacing guide while the line, dots and labels are hidden.

## Settings (GridGenerator component)

- **Line Width** 0.02, **Line Color** grey, **Line Alpha** 0.5.
- **Lane Half Width** 1 (scene value; graph-local units) — padding beyond the value range.
- Spacing comes from `GraphSettings.spacing`; the graph's uniform scale sets the real
  distance (walk length / (points − 1)).

## Notes

- Each graph generation clears the old grid and builds a new one; changing settings on
  the start screen clears it too (M68).
- The baseline is drawn at value 0, which can lie outside the area for data far from 0.
