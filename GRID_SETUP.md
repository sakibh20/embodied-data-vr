# Grid System Setup Guide

## Overview
The grid system displays guidance lines on the ground synchronized with your graph data points. It helps visualize the spacing between data points that the user will walk.

## Integration

### In Your Scene (Test.unity or any scene)

1. **Add Grid GameObject**
   - Right-click in hierarchy → 3D Object → Create Empty
   - Name it "Grid"
   - Position it at (0, 0, 0) or wherever your graph is

2. **Add GridGenerator Component**
   - Select the Grid object
   - Add Component → GridGenerator

3. **Wire GraphManager**
   - Select GraphManager in the scene
   - In the Inspector, find the empty "Grid Generator" slot
   - Drag the Grid object (or its GridGenerator component) into it

### Result
When you press "GenerateGraph" (ContextMenu on GraphManager), it will:
1. Generate the graph dots and lines
2. Automatically generate the synchronized grid after animation completes

## Grid Customization

In the GridGenerator component:
- **Line Width**: Thickness of grid lines (default 0.02)
- **Line Color**: Color of grid lines (default gray)
- **Line Alpha**: Transparency (0-1, default 0.5)

## How It Works

- Grid width = number of data points in your GraphData
- Grid spacing = GraphSettings.spacing value
- Grid aligns perfectly with data point positions
- Vertical lines mark each data point location
- Horizontal reference line at Z=0 for orientation

## Notes

- Grid is generated as child objects of the Grid GameObject (GridLine prefabs)
- Each call to GenerateGraph clears old grid and creates new one
- Grid uses shared material for efficiency
- Works with any GraphSettings and GraphData configuration
