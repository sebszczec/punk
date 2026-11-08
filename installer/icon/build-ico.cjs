// Renders punk-icon.svg to PNGs and packs them into punk.ico (PNG-compressed entries, Windows Vista+).
// Needs @resvg/resvg-js, which is NOT committed. Run it with the module from a scratch folder:
//   npm install --prefix <scratch> @resvg/resvg-js
//   NODE_PATH=<scratch>/node_modules node installer/icon/build-ico.cjs
const fs = require('node:fs');
const path = require('node:path');
const { Resvg } = require('@resvg/resvg-js');

const here = __dirname;
const svg = fs.readFileSync(path.join(here, 'punk-icon.svg'), 'utf8');
const sizes = [16, 24, 32, 48, 64, 128, 256];

const pngs = sizes.map((size) => ({
  size,
  png: new Resvg(svg, { fitTo: { mode: 'width', value: size } }).render().asPng(),
}));

// ICO header: reserved(2) type=1(2) count(2), then one 16-byte directory entry per image.
const header = Buffer.alloc(6 + 16 * pngs.length);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(pngs.length, 4);

let offset = header.length;
pngs.forEach(({ size, png }, i) => {
  const e = 6 + 16 * i;
  header.writeUInt8(size === 256 ? 0 : size, e);       // width (0 means 256)
  header.writeUInt8(size === 256 ? 0 : size, e + 1);   // height
  header.writeUInt8(0, e + 2);                         // palette colours
  header.writeUInt8(0, e + 3);                         // reserved
  header.writeUInt16LE(1, e + 4);                      // colour planes
  header.writeUInt16LE(32, e + 6);                     // bits per pixel
  header.writeUInt32LE(png.length, e + 8);             // image size
  header.writeUInt32LE(offset, e + 12);                // image offset
  offset += png.length;
});

const ico = Buffer.concat([header, ...pngs.map((p) => p.png)]);
fs.writeFileSync(path.join(here, '..', 'punk.ico'), ico);
console.log(`wrote installer/punk.ico (${ico.length} bytes, sizes ${sizes.join(', ')})`);
