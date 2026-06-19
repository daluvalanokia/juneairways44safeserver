/**
 * pull-from-44.mjs
 * Downloads all source files from juneairways44safeserver into the local workspace,
 * skipping binary DB files and .git internals.
 */
import { ReplitConnectors } from "@replit/connectors-sdk";
import fs from "fs";
import path from "path";

const connectors = new ReplitConnectors();
const OWNER = "daluvalanokia";
const REPO  = "juneairways44safeserver";

const SKIP_PATHS = new Set([
  // keep local DB and artifact config
  "artifacts/airways-mergesafe/AirwaysMergeSafeServer/mergesafe.db",
  "artifacts/airways-mergesafe/.replit-artifact/artifact.edit.toml",
  "README.md",
]);
const SKIP_EXTS = new Set([".db", ".db-shm", ".db-wal"]);

const delay = ms => new Promise(r => setTimeout(r, ms));

async function getBlob(blobUrl, retries = 5) {
  for (let i = 1; i <= retries; i++) {
    const endpoint = blobUrl.replace("https://api.github.com", "");
    const resp = await connectors.proxy("github", endpoint, {
      method: "GET",
      headers: { Accept: "application/vnd.github.raw" },
    });
    const text = await resp.text();
    // Check for rate limit
    if (text.includes('"Rate limit exceeded"') || text.includes('"rate limit"')) {
      const wait = i * 1500;
      process.stdout.write(`[rl retry ${i}]`);
      await delay(wait);
      continue;
    }
    return text;
  }
  throw new Error("Rate limit exceeded after retries");
}

async function main() {
  // 1. Get full tree
  const treeResp = await connectors.proxy("github",
    `/repos/${OWNER}/${REPO}/git/trees/main?recursive=1`, { method: "GET" });
  const tree = await treeResp.json();
  if (!tree.tree) { console.error("Failed to get tree:", tree); process.exit(1); }

  const blobs = tree.tree.filter(f =>
    f.type === "blob" &&
    !SKIP_PATHS.has(f.path) &&
    !SKIP_EXTS.has(path.extname(f.path).toLowerCase())
  );

  console.log(`Files to pull: ${blobs.length}`);

  // 2. Download in batches of 3
  const BATCH = 3;
  let ok = 0, failed = 0;
  for (let i = 0; i < blobs.length; i += BATCH) {
    await delay(200);
    const batch = blobs.slice(i, i + BATCH);
    await Promise.all(batch.map(async (item) => {
      try {
        const content = await getBlob(item.url);
        const localPath = item.path;
        const dir = path.dirname(localPath);
        if (dir && dir !== ".") fs.mkdirSync(dir, { recursive: true });

        // If content is JSON (not raw), decode base64
        let toWrite;
        try {
          const j = JSON.parse(content);
          if (j.content && j.encoding === "base64") {
            toWrite = Buffer.from(j.content.replace(/\n/g, ""), "base64");
          } else {
            toWrite = content;
          }
        } catch {
          toWrite = content;
        }

        fs.writeFileSync(localPath, toWrite);
        process.stdout.write(".");
        ok++;
      } catch (e) {
        console.error(`\nFAILED ${item.path}: ${e.message}`);
        failed++;
      }
    }));
  }

  console.log(`\nDone. OK=${ok} Failed=${failed}`);
}

main().catch(e => { console.error(e); process.exit(1); });
