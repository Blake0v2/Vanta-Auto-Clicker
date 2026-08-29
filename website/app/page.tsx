import type { CSSProperties } from 'react';
import { ArrowDownToLine, ArrowUpRight, Expand } from 'lucide-react';
import { buttonVariants } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';

const DOWNLOAD_URL =
  'https://github.com/Blake0v2/Vanta-Auto-Clicker/releases/latest/download/Vanta.Auto.Clicker.Setup.exe';
const REPOSITORY_URL = 'https://github.com/Blake0v2/Vanta-Auto-Clicker';
const APP_PREVIEW_VERSION = '1.0.4';

type AppPreviewProps = {
  view: 'Default' | 'Advanced';
  height: number;
  description: string;
  eager?: boolean;
};

function AppPreview({ view, height, description, eager = false }: AppPreviewProps) {
  const src = `/images/vanta-${view.toLowerCase()}.png`;

  return (
    <figure className="app-preview">
      <Dialog>
        <DialogTrigger
          className="screenshot-button"
          aria-label={`Enlarge ${view} view screenshot`}
        >
          <span
            className="screenshot-crop"
            style={{ '--shot-ratio': `980 / ${height - 20}` } as CSSProperties}
          >
            <img
              src={src}
              width="1000"
              height={height}
              alt={`Vanta Auto Clicker ${view} view. ${description}`}
              loading={eager ? 'eager' : 'lazy'}
              fetchPriority={eager ? 'high' : 'auto'}
              decoding="async"
            />
          </span>
          <span className="preview-action" aria-hidden="true">
            <Expand size={15} />
          </span>
        </DialogTrigger>
        <figcaption>
          <span>{view} view</span>
          <span className="preview-caption-note">Click to enlarge <ArrowUpRight size={12} aria-hidden="true" /></span>
        </figcaption>
        <DialogContent className="screenshot-dialog">
          <DialogTitle>{view} view</DialogTitle>
          <DialogDescription>
            {description} Screenshot from Vanta {APP_PREVIEW_VERSION}.
          </DialogDescription>
          <div className="full-screenshot">
            <img
              src={src}
              width="1000"
              height={height}
              alt={`Full-size Vanta Auto Clicker ${view} view`}
            />
          </div>
        </DialogContent>
      </Dialog>
    </figure>
  );
}

export default function Home() {
  return (
    <>
      <a href="#main" className="skip-link">Skip to content</a>

      <header className="site-header">
        <a className="brand" href="#main" aria-label="Vanta Auto Clicker home">
          <img src="/images/vanta-logo.png" width="48" height="48" alt="" />
          <span>Vanta</span>
        </a>
        <nav className="site-nav" aria-label="Main navigation">
          <a className="nav-preview" href="#advanced">The app</a>
          <a href={REPOSITORY_URL} target="_blank" rel="noreferrer">
            GitHub <ArrowUpRight size={13} aria-hidden="true" />
          </a>
          <a className="nav-download" href={DOWNLOAD_URL}>Download</a>
        </nav>
      </header>

      <main id="main" className="wrap">
        <section className="hero" aria-labelledby="app-name">
          <div className="hero-copy">
            <p className="eyebrow">A Windows desktop app</p>
            <h1 id="app-name">Vanta<br />Auto Clicker</h1>
            <p className="hero-description">
              A straightforward auto clicker for Windows. Set your timing,
              pick a shortcut, and let Vanta handle the repetition.
            </p>
            <div className="download-area">
              <a className={buttonVariants({ className: 'download-button' })} href={DOWNLOAD_URL}>
                <ArrowDownToLine size={18} aria-hidden="true" />
                Download for Windows
              </a>
              <p className="download-note">Latest release <span aria-hidden="true">/</span> Windows 10 &amp; 11 <span aria-hidden="true">/</span> Setup .exe</p>
            </div>
          </div>

          <div className="hero-preview">
            <AppPreview
              view="Default"
              height={460}
              description="Click timing, hotkeys, and mouse controls in one compact view."
              eager
            />
            <p className="preview-footnote">
              Click speed, mouse button, and hotkey in one view.
            </p>
          </div>
        </section>

        <section className="advanced-section" id="advanced" aria-labelledby="advanced-title">
          <div className="advanced-copy">
            <p className="eyebrow">The advanced view</p>
            <h2 id="advanced-title">Sequences,<br />limits &amp; more.</h2>
            <p className="section-description">
              Save cursor positions and click through them in order.
              Set a click or time limit, adjust double clicks, and
              add variation to your timing.
            </p>
            <p className="section-description secondary-description">
              Both views share your settings, so you can switch back
              whenever you like.
            </p>
            <div className="keyboard-note">
              <kbd>F6</kbd>
              <span>Capture a cursor position in advanced view.</span>
            </div>
          </div>
          <div className="advanced-preview">
            <AppPreview
              view="Advanced"
              height={840}
              description="Sequence clicking, click and time limits, timing variation, and double clicks."
            />
          </div>
        </section>

        <section className="quick-start" aria-labelledby="quick-start-title">
          <div className="quick-start-copy">
            <h2 id="quick-start-title">Getting started</h2>
            <p>Run Setup once, then open Vanta from the Start menu or desktop. No account or administrator access required.</p>
          </div>
          <dl className="shortcut-list">
            <div><dt><kbd>F8</kbd></dt><dd>Start / stop <span>Default shortcut</span></dd></div>
            <div><dt><kbd>Esc</kbd></dt><dd>Stop clicking <span>Any time during a run</span></dd></div>
          </dl>
        </section>
      </main>

      <footer className="site-footer wrap">
        <div className="footer-top">
          <span className="footer-name">Vanta Auto Clicker</span>
          <a href={REPOSITORY_URL + '/releases'} target="_blank" rel="noreferrer">
            Release notes <ArrowUpRight size={13} aria-hidden="true" />
          </a>
        </div>
        <div className="footer-details">
          <p>Requires .NET Framework 4.8+. The app is unsigned; keep your security protections enabled.</p>
          <p>App previews: {APP_PREVIEW_VERSION} <span aria-hidden="true">·</span> Linked download: latest GitHub Release</p>
        </div>
      </footer>
    </>
  );
}
