# Vanta Auto Clicker website

A spacious product page for Vanta Auto Clicker. It uses the original supplied Vanta_Logo.png unchanged, two real app screenshots, Paytone One headings, black surfaces, and light-blue outlines. The desktop layout pairs the download with the default view, then shows the advanced view beside a short feature explanation. On small screens, the sections stack. Both screenshots open in accessible enlargement dialogs.

The website is now a static Vite/React site prepared for Netlify. It has no ChatGPT/Sites or Cloudflare runtime dependency. The previous private ChatGPT preview has not been updated or made public.

## Upload to Netlify

1. Sign in to your Netlify account.
2. Open https://app.netlify.com/drop.
3. Upload Vanta-Website-Netlify.zip, or extract it and upload the folder containing index.html.
4. Follow Netlify's publishing prompt.

The ZIP contains the already-built site. There is no build step required for that upload. No Netlify account is connected in this workspace, so preparing the package does not publish it.

Official instructions: https://docs.netlify.com/start/quickstarts/netlify-drop-quickstart/

## Develop and build

Run npm install, then npm run dev. Run npm run build to type-check and generate dist. The build produces ordinary HTML, CSS, JavaScript, images, and fonts. The netlify.toml configuration uses npm run build and publishes dist.

If you connect this website directory as its own Git repository, leave the base directory unset. If you put it inside the Windows app repository, set the Netlify base directory to website, the build command to npm run build, and the publish directory to dist.

For a Netlify Git build, the URL environment variable supplies the absolute sharing-image URL automatically. For other build systems, set VITE_SITE_URL to the final origin. A manual upload made before its Netlify URL is known uses a relative image URL; rebuild with VITE_SITE_URL after the final URL is assigned if an absolute social image URL is needed.

## Content

- app/page.tsx contains the page and the exact supplied GitHub Setup EXE download link.
- app/globals.css contains the theme and responsive layout.
- index.html contains title, description, favicon, and no-JavaScript download fallback.
- vite.config.ts adds sharing metadata using only trusted build settings.
- public/images/vanta-logo.png is byte-for-byte identical to the user's original app logo. The favicon and sharing metadata point to this exact file. No generated replacement logo is used.
- Screenshots show desktop build 1.0.4. The download button uses GitHub's permanent latest-release URL, so it follows future stable releases that contain `Vanta.Auto.Clicker.Setup.exe`.
- public/fonts contains the original Paytone One font and its SIL Open Font License.
