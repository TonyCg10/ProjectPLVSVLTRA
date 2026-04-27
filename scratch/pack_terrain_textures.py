#!/usr/bin/env python3
"""
Channel packs terrain textures for Terrain3D.
Output format:
  - albedo_height.png  = Color(RGB) + Displacement(A)
  - normal_rough.png   = NormalGL(RGB) + Roughness(A)
"""
from PIL import Image
import os

BASE = "/run/media/toni/6EF68777F6873DF9/LINUX/CODE/ProjectPLVSVLTRA/Assets/Textures"
OUT  = "/run/media/toni/6EF68777F6873DF9/LINUX/CODE/ProjectPLVSVLTRA/Assets/Textures/Terrain3D_Packed"

os.makedirs(OUT, exist_ok=True)

# Define texture sets: (name, color, normalGL, roughness, displacement_or_None)
SETS = [
    {
        "name": "grass",
        "color":  f"{BASE}/Grass004_2K-JPG/Grass004_2K-JPG_Color.jpg",
        "normal": f"{BASE}/Grass004_2K-JPG/Grass004_2K-JPG_NormalGL.jpg",
        "rough":  f"{BASE}/Grass004_2K-JPG/Grass004_2K-JPG_Roughness.jpg",
        "disp":   f"{BASE}/Grass004_2K-JPG/Grass004_2K-JPG_Displacement.jpg",
    },
    {
        "name": "rock",
        "color":  f"{BASE}/Rock058_2K-JPG/Rock058_2K-JPG_Color.jpg",
        "normal": f"{BASE}/Rock058_2K-JPG/Rock058_2K-JPG_NormalGL.jpg",
        "rough":  f"{BASE}/Rock058_2K-JPG/Rock058_2K-JPG_Roughness.jpg",
        "disp":   f"{BASE}/Rock058_2K-JPG/Rock058_2K-JPG_Displacement.jpg",
    },
    {
        "name": "ground",
        "color":  f"{BASE}/Ground093C_2K-JPG/Ground093C_2K-JPG_Color.jpg",
        "normal": f"{BASE}/Ground093C_2K-JPG/Ground093C_2K-JPG_NormalGL.jpg",
        "rough":  f"{BASE}/Ground093C_2K-JPG/Ground093C_2K-JPG_Roughness.jpg",
        "disp":   f"{BASE}/Ground093C_2K-JPG/Ground093C_2K-JPG_Displacement.jpg",
    },
    {
        "name": "terrain_soil",
        "color":  f"{BASE}/Terrain001_2K-PNG/Terrain001_2K_Color.png",
        "normal": None,  # No normal map available
        "rough":  None,  # No roughness available
        "disp":   None,
    },
]

def pack_albedo_height(color_path, disp_path, output_path):
    """Pack Color(RGB) + Displacement(A) into RGBA PNG."""
    color = Image.open(color_path).convert("RGB")
    w, h = color.size
    
    if disp_path and os.path.exists(disp_path):
        disp = Image.open(disp_path).convert("L").resize((w, h))
    else:
        # No displacement: use flat mid-gray
        disp = Image.new("L", (w, h), 128)
    
    # Merge RGB + Alpha
    r, g, b = color.split()
    packed = Image.merge("RGBA", (r, g, b, disp))
    packed.save(output_path, "PNG")
    print(f"  ✅ {os.path.basename(output_path)} ({w}x{h})")

def pack_normal_rough(normal_path, rough_path, output_path, size=(2048, 2048)):
    """Pack NormalGL(RGB) + Roughness(A) into RGBA PNG."""
    if normal_path and os.path.exists(normal_path):
        normal = Image.open(normal_path).convert("RGB")
        w, h = normal.size
    else:
        # Default flat normal (pointing up)
        w, h = size
        normal = Image.new("RGB", (w, h), (128, 128, 255))
    
    if rough_path and os.path.exists(rough_path):
        rough = Image.open(rough_path).convert("L").resize((w, h))
    else:
        # Default medium roughness
        rough = Image.new("L", (w, h), 180)
    
    r, g, b = normal.split()
    packed = Image.merge("RGBA", (r, g, b, rough))
    packed.save(output_path, "PNG")
    print(f"  ✅ {os.path.basename(output_path)} ({w}x{h})")

print("=== Terrain3D Texture Channel Packer ===\n")

for s in SETS:
    name = s["name"]
    print(f"Packing [{name}]...")
    
    albedo_out = os.path.join(OUT, f"{name}_albedo_height.png")
    normal_out = os.path.join(OUT, f"{name}_normal_rough.png")
    
    pack_albedo_height(s["color"], s.get("disp"), albedo_out)
    pack_normal_rough(s.get("normal"), s.get("rough"), normal_out)
    print()

print(f"✅ All packed textures saved to:\n   {OUT}")
print("\nNext: In Godot, go to Terrain3D Assets → add texture slots")
print("and assign each pair (albedo_height + normal_rough).")
