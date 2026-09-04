"""
QDE-022 â Etape 1/2 : construction de la grille 1 seconde.

Lit les fichiers DBN mbp-10 (ESU6, aout 2026), calcule les 4 indicateurs geles
en QDE-022 v4 Â§3, et ecrit un Parquet par jour contenant uniquement la grille
1 s. Les fichiers bruts ne sont plus relus ensuite.

Conforme au gel :
  - horodatage ts_recv uniquement (v4 Â§8, correctness point-in-time)
  - session RTH 09:30-16:00 America/New_York
  - aucun report inter-jour
  - aucun calcul de P&L, de strategie ou de Sharpe

Usage :
    python qde022_build_grid.py <dossier_dbn> <dossier_sortie>
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
import pandas as pd
import databento as db

# --- Constantes gelees -------------------------------------------------------

TICK_SIZE = 0.25          # ES
N_LEVELS = 10             # mbp-10
TZ = "America/New_York"
RTH_START = pd.Timestamp("09:30:00").time()
RTH_END = pd.Timestamp("16:00:00").time()

# Taille des lots de lecture. A baisser si la memoire sature.
CHUNK = 2_000_000


def _detect_px_scale(sample: pd.Series) -> float:
    """databento encode les prix en virgule fixe 1e-9. Selon la version de la
    lib, to_df() peut deja avoir applique la conversion. On tranche sur l'ordre
    de grandeur plutot que sur la version."""
    v = float(sample.dropna().abs().max())
    return 1e-9 if v > 1e6 else 1.0


def _iter_day(path: Path):
    """Rend des DataFrames par lots pour eviter de charger 7 M de lignes d'un
    coup. Retombe sur un chargement unique si la lib ne supporte pas `count`."""
    store = db.DBNStore.from_file(path)
    try:
        yield from store.to_df(count=CHUNK)
    except TypeError:
        yield store.to_df()


def compute_ofi(df: pd.DataFrame, scale: float, levels: int) -> np.ndarray:
    """Order Flow Imbalance, Cont-Kukanov-Stoikov (2014), somme sur `levels`
    niveaux avec des poids egaux.

    Pour chaque niveau m et chaque mise a jour n :
        e = 1[Pb_n >= Pb_{n-1}] * Qb_n - 1[Pb_n <= Pb_{n-1}] * Qb_{n-1}
          - 1[Pa_n <= Pa_{n-1}] * Qa_n + 1[Pa_n >= Pa_{n-1}] * Qa_{n-1}
    """
    total = np.zeros(len(df), dtype=np.float64)

    for m in range(levels):
        bid_px = df[f"bid_px_{m:02d}"].to_numpy(np.float64) * scale
        ask_px = df[f"ask_px_{m:02d}"].to_numpy(np.float64) * scale
        bid_sz = df[f"bid_sz_{m:02d}"].to_numpy(np.float64)
        ask_sz = df[f"ask_sz_{m:02d}"].to_numpy(np.float64)

        pb_prev, pa_prev = np.roll(bid_px, 1), np.roll(ask_px, 1)
        qb_prev, qa_prev = np.roll(bid_sz, 1), np.roll(ask_sz, 1)

        e = (
            (bid_px >= pb_prev) * bid_sz
            - (bid_px <= pb_prev) * qb_prev
            - (ask_px <= pa_prev) * ask_sz
            + (ask_px >= pa_prev) * qa_prev
        )
        e[0] = 0.0  # pas d'etat precedent sur la premiere ligne du jour
        total += e

    return total


def build_day(path: Path) -> pd.DataFrame | None:
    """Un fichier DBN journalier -> une grille 1 s."""
    buckets: list[pd.DataFrame] = []
    scale: float | None = None

    for chunk in _iter_day(path):
        if chunk.empty:
            continue
        if scale is None:
            scale = _detect_px_scale(chunk["bid_px_00"])

        # ts_recv : instant ou l'information nous etait disponible.
        ts = chunk.index if chunk.index.name == "ts_recv" else chunk["ts_recv"]
        ts = pd.to_datetime(ts, utc=True).tz_convert(TZ)

        df = chunk.copy()
        df["ts"] = ts

        # RTH strict, jours ouvres uniquement.
        t = df["ts"].dt.time
        df = df[(t >= RTH_START) & (t < RTH_END) & (df["ts"].dt.dayofweek < 5)]
        if df.empty:
            continue

        bid_px = df["bid_px_00"].to_numpy(np.float64) * scale
        ask_px = df["ask_px_00"].to_numpy(np.float64) * scale
        bid_sz = df["bid_sz_00"].to_numpy(np.float64)
        ask_sz = df["ask_sz_00"].to_numpy(np.float64)

        valid = (bid_px > 0) & (ask_px > 0) & (ask_px >= bid_px)
        df = df[valid]
        if df.empty:
            continue
        bid_px, ask_px = bid_px[valid], ask_px[valid]
        bid_sz, ask_sz = bid_sz[valid], ask_sz[valid]

        # Indicateur 1 et 2 : OFI 1 niveau et 10 niveaux.
        df["ofi_l1"] = compute_ofi(df, scale, levels=1)
        df["ofi_l10"] = compute_ofi(df, scale, levels=N_LEVELS)

        # Indicateur 3 : desequilibre statique du carnet sur 10 niveaux.
        bsum = sum(df[f"bid_sz_{m:02d}"].to_numpy(np.float64) for m in range(N_LEVELS))
        asum = sum(df[f"ask_sz_{m:02d}"].to_numpy(np.float64) for m in range(N_LEVELS))
        denom = bsum + asum
        df["book_imb"] = np.where(denom > 0, (bsum - asum) / denom, 0.0)

        # Indicateur 4 : ecart micro-prix / mid, en ticks.
        mid = 0.5 * (bid_px + ask_px)
        qsum = bid_sz + ask_sz
        micro = np.where(qsum > 0, (bid_px * ask_sz + ask_px * bid_sz) / qsum, mid)
        df["mid"] = mid
        df["micro_dev"] = (micro - mid) / TICK_SIZE

        # Agregation sur la grille 1 s.
        df["sec"] = df["ts"].dt.floor("1s")
        g = df.groupby("sec", sort=True).agg(
            ofi_l1=("ofi_l1", "sum"),        # flux : on somme sur la seconde
            ofi_l10=("ofi_l10", "sum"),
            book_imb=("book_imb", "last"),   # etat : dernier observe
            micro_dev=("micro_dev", "last"),
            mid=("mid", "last"),
            n_upd=("mid", "size"),
        )
        buckets.append(g)

    if not buckets:
        return None

    out = pd.concat(buckets)
    out = out.groupby(level=0).agg(
        ofi_l1=("ofi_l1", "sum"),
        ofi_l10=("ofi_l10", "sum"),
        book_imb=("book_imb", "last"),
        micro_dev=("micro_dev", "last"),
        mid=("mid", "last"),
        n_upd=("n_upd", "sum"),
    )
    out.index.name = "sec"
    out["date"] = out.index.date
    return out.reset_index()


def main() -> None:
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)

    src, dst = Path(sys.argv[1]), Path(sys.argv[2])
    dst.mkdir(parents=True, exist_ok=True)

    files = sorted(src.glob("*.dbn.zst"))
    if not files:
        print(f"Aucun fichier .dbn.zst dans {src}")
        sys.exit(1)

    print(f"{len(files)} fichiers a traiter.\n")

    for i, path in enumerate(files, 1):
        target = dst / f"{path.name.split('.')[0]}.parquet"
        if target.exists():
            print(f"[{i:2d}/{len(files)}] {path.name} -> deja fait, ignore")
            continue

        grid = build_day(path)
        if grid is None or grid.empty:
            print(f"[{i:2d}/{len(files)}] {path.name} -> aucun point RTH")
            continue

        grid.to_parquet(target, index=False)
        print(
            f"[{i:2d}/{len(files)}] {path.name} -> {len(grid):>6d} points, "
            f"{grid['n_upd'].sum():>10,d} maj carnet"
        )

    print(f"\nTermine. Grilles ecrites dans {dst}")


if __name__ == "__main__":
    main()
