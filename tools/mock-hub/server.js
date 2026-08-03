// Basit sahte API Hub - Kisi 3 UI'sinin HttpPipelineSource yolunu Kisi 2'nin
// gercek FastAPI'si olmadan test etmek icin. HttpPipelineSource'un bekledigi
// birlestirilmis JSON sozlesmesini uretir (bkz. HttpPipelineSource.cs).
//
// Calistir:   node tools/mock-hub/server.js
// Sonra UI:   $env:SCaDa_HUB_URL="http://localhost:5099"; ...SCaDaDashboard.exe
//
// Sadece Node standart http modulu.

const http = require("http");

const PORT = 5099;
const MAX_RUL = 130;

function sensorsFrom(rul) {
  const d = Math.max(0, Math.min(1, 1 - rul / MAX_RUL));
  return {
    vibration: +(2 + d * 7).toFixed(2),
    bearingTemp: +(70 + d * 28).toFixed(1),
    dischargePressure: +(75 - d * 10).toFixed(2),
  };
}

function state() {
  const t = Date.now() / 1000;
  const n2rul = Math.round(65 + 55 * (0.5 + 0.5 * Math.sin(t / 25)));
  const n4rul = Math.round(10 + 40 * (0.5 + 0.5 * Math.sin(t / 15 + 1)));
  const node = (id, rul) => ({
    id,
    rul,
    health: +Math.max(0, Math.min(100, (rul / MAX_RUL) * 100)).toFixed(1),
    ...sensorsFrom(rul),
  });

  const seg = (id, base, leak = false) => {
    const flow = +(base * (0.8 + 0.3 * (0.5 + 0.5 * Math.sin(t / 10)))).toFixed(1);
    return { id, flow, load: +Math.min(1, flow / 60).toFixed(2), leak };
  };

  return {
    nodes: [node("N2", n2rul), node("N4", n4rul)],
    segments: [
      seg("S1", 55), seg("S2", 50), seg("S3", 22),
      seg("S4", 30, Math.sin(t / 8) > 0.6), seg("S5", 18),
      seg("S6", 20), seg("S7", 15),
    ],
  };
}

http.createServer((req, res) => {
  res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
  res.end(JSON.stringify(state()));
}).listen(PORT, () => console.log(`Mock Hub: http://localhost:${PORT}`));
