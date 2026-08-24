from pathlib import Path
from PIL import Image

repository_root = Path(__file__).resolve().parents[2]
brand_root = repository_root / "src" / "LavanderiaAnjos.Web" / "wwwroot" / "assets" / "brand"
source = Image.open(brand_root / "lavanderia-anjos-512.png").convert("RGBA")
source.save(brand_root / "favicon.ico", format="ICO", sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
