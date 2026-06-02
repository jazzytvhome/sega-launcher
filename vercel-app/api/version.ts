import type { VercelRequest, VercelResponse } from "@vercel/node";

// Public version manifest. Bump MIN_VERSION in Vercel env when you ship an update
// that should force old launchers to update. DOWNLOAD_URL points users to the new build.
export default function handler(_req: VercelRequest, res: VercelResponse) {
  res.status(200).json({
    min: process.env.MIN_VERSION ?? "0.0.0",
    latest: process.env.LATEST_VERSION ?? process.env.MIN_VERSION ?? "0.0.0",
    url: process.env.DOWNLOAD_URL ?? "",
  });
}
