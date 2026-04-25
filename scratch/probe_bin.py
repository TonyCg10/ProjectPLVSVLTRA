import struct
import os

path = "/run/media/toni/6EF68777F6873DF9/LINUX/CODE/ProjectPLVSVLTRA/data/map/world_data.bin"
size = os.path.getsize(path)
print(f"Size: {size} bytes")

with open(path, "rb") as f:
    data = f.read(1000)

print("\n--- Try 4-byte stride (USHORT, USHORT) ---")
for i in range(10):
    s, c = struct.unpack_from("<HH", data, i*4)
    print(f"[{i}] State: {s}, Country: {c}")

print("\n--- Try 8-byte stride (USHORT, USHORT, FLOAT?) ---")
for i in range(10):
    s, c, h = struct.unpack_from("<HHf", data, i*8)
    print(f"[{i}] State: {s}, Country: {c}, Height(?): {h}")

print("\n--- Try 20-byte stride (USHORT, USHORT, 4x FLOAT) ---")
for i in range(5):
    try:
        s, c, f1, f2, f3, f4 = struct.unpack_from("<HHffff", data, i*20)
        print(f"[{i}] S: {s}, C: {c}, F: {f1:.2f}, {f2:.2f}, {f3:.2f}, {f4:.2f}")
    except:
        break

# Check for repeating patterns
print("\n--- Checking for periodicity ---")
def find_period(data, max_p=5000):
    for p in range(4, max_p):
        if data[:p] == data[p:2*p] == data[2*p:3*p]:
            return p
    return None

p = find_period(data)
if p:
    print(f"Repeating pattern found with period: {p} bytes")
else:
    print("No simple repeating pattern found in the first 1000 bytes.")

# Check common resolutions
resolutions = [
    (1024, 1024), (2048, 1024), (4096, 2048), (3600, 1800), (5400, 2700), (2560, 1280)
]
for w, h in resolutions:
    if size % (w*h) == 0:
        print(f"Matches resolution {w}x{h} with {size//(w*h)} bytes per pixel.")
