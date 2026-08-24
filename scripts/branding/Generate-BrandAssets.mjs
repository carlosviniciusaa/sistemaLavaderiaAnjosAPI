import { readFile } from "node:fs/promises";
import path from "node:path";

const { default: sharp } = await import(process.env.SHARP_MODULE || "sharp");

const repositoryRoot = path.resolve(import.meta.dirname, "../..");
const brandRoot = path.join(repositoryRoot, "src/LavanderiaAnjos.Web/wwwroot/assets/brand");
const sourceSvg = await readFile(path.join(brandRoot, "lavanderia-anjos-color.svg"));

for (const size of [16, 32, 48, 180, 192, 512]) {
  const name = size === 180 ? "apple-touch-icon.png" : `lavanderia-anjos-${size}.png`;
  await sharp(sourceSvg, { density: Math.max(96, Math.ceil(size * 72 / 160)) })
    .resize(size, size, { fit: "contain" })
    .png({ compressionLevel: 9 })
    .toFile(path.join(brandRoot, name));
}
