#!/usr/bin/env python3
"""Convert favicon.svg to favicon.ico for WinForms app."""

import sys
from pathlib import Path
from PIL import Image
import cairosvg
import io

def svg_to_ico(svg_path: str, ico_path: str) -> None:
    """Convert SVG to multi-size ICO file."""
    svg_file = Path(svg_path)
    ico_file = Path(ico_path)

    if not svg_file.exists():
        print(f"Error: SVG file not found: {svg_path}")
        sys.exit(1)

    # Sizes for ICO file (common sizes for Windows)
    sizes = [16, 32, 48, 64, 128, 256]
    images = []

    print(f"Converting {svg_file.name} to ICO...")

    for size in sizes:
        print(f"  Rendering {size}x{size}...", end=" ", flush=True)

        # Convert SVG to PNG in memory
        png_data = io.BytesIO()
        cairosvg.svg2png(
            url=str(svg_file),
            write_to=png_data,
            output_width=size,
            output_height=size
        )
        png_data.seek(0)

        # Open PNG and convert to RGBA if needed
        img = Image.open(png_data)
        if img.mode != 'RGBA':
            img = img.convert('RGBA')

        images.append(img)
        print("ok")

    # Save as ICO
    images[-1].save(
        ico_file,
        format='ICO',
        sizes=[(img.width, img.height) for img in images]
    )

    print(f"\nok Successfully created {ico_file.name}")
    print(f"  Icon with {len(sizes)} sizes (16x16 to 256x256)")

if __name__ == "__main__":
    svg_path = Path(__file__).resolve().parent.parent / "src" / "PtcXaeMcp.WinForms" / "favicon.svg"
    ico_path = Path(__file__).resolve().parent.parent / "src" / "PtcXaeMcp.WinForms" / "favicon.ico"

    svg_to_ico(str(svg_path), str(ico_path))
