"""
QDE-022 â Etape 2/2 : courbe de decroissance de l'IC.

Lit les grilles 1 s produites par qde022_build_grid.py et calcule, pour chaque
couple (indicateur, horizon), la correlation de Pearson avec le rendement futur
du mid, assortie d'un intervalle de confiance a 95 % corrige Newey-West.

Conforme au gel QDE-022 v4 :
  Â§3  4 indicateurs, z-score calibre sur TRAIN seul
  Â§4  6 horizons : 1, 5, 30, 60, 300, 900 s
  Â§5  24 tests, correction de Holm, alpha = 0,05
  Â§6  metrique primaire = IC. Pas de backtest, pas de P&L, pas de Sharpe.
  Â§7  gate economique : 0,80 tick ES. Reference = ES. Reporte aussi en MES.

Usage :
    python qde022_ic_curve.py <dossier_grilles> [dossier_sortie]
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
import pandas as pd

# --- Parametres geles --------------------------------------------------------

INDICATORS = ["ofi_l1", "ofi_l10", "book_imb", "micro_dev"]
HORIZONS = [1, 5, 30, 60, 300, 900]          # secondes
ALPHA = 0.05

TICK_ES = 0.25
GATE_ES_TICKS = 0.80                          # 2x cout AR ES
GATE_MES_TICKS = 6.16                         # 2x cout AR MES, pour report seul

TRAIN_FRAC = 0.60
PURGE_DAYS = 1

# Facteur de conversion IC -> gain attendu par trade (v4 Â§7).
# 0,80 = sqrt(2/pi), regime "tous les signaux tradÃ©s".
K_ALL_SIGNALS = np.sqrt(2.0 / np.pi)


def load_grids(folder: Path) -> pd.DataFrame:
    files = sorted(folder.glob("*.parquet"))
    if not files:
        raise SystemExit(f"Aucun parquet dans {folder}")
    df = pd.concat((pd.read_parquet(f) for f in files), ignore_index=True)
    df["sec"] = pd.to_datetime(df["sec"])
    return df.sort_values("sec").reset_index(drop=True)


def forward_returns(df: pd.DataFrame, h: int) -> np.ndarray:
    """Rendement log du mid sur h secondes, strictement intra-journalier.

    La grille est reindexee a la seconde pleine par jour, ce qui evite de
    compter h positions de tableau la ou il manque des secondes creuses.

    Travaille exclusivement par POSITION, jamais par label d'index : un
    DataFrame issu d'un filtre booleen conserve les labels d'origine, qui ne
    sont pas des positions valides dans le tableau de sortie.
    """
    out = np.full(len(df), np.nan)
    dates = df["date"].to_numpy()

    for d in pd.unique(dates):
        idx = np.flatnonzero(dates == d)
        day = df.iloc[idx]
        secs = day["sec"].to_numpy().astype("datetime64[s]").astype(np.int64)
        mid = day["mid"].to_numpy(np.float64)

        base = secs[0]
        pos = secs - base
        span = int(pos[-1]) + 1

        lookup = np.full(span, np.nan)
        lookup[pos] = mid

        tgt = pos + h
        ok = tgt < span
        fut = np.full(len(day), np.nan)
        fut[ok] = lookup[tgt[ok]]

        with np.errstate(divide="ignore", invalid="ignore"):
            out[idx] = np.log(fut / mid)

    return out


def newey_west_ic(x: np.ndarray, y: np.ndarray, lags: int) -> tuple[float, float, float, float]:
    """IC de Pearson + erreur-type HAC.

    Les rendements a horizon h se chevauchent sur une grille de 1 s, ce qui
    autocorrele fortement les residus. Sans correction, l'IC parait bien plus
    significatif qu'il ne l'est. On prend h retards.

    Retourne (ic, se, lo95, hi95).
    """
    ok = np.isfinite(x) & np.isfinite(y)
    x, y = x[ok], y[ok]
    n = len(x)
    if n < 100 or x.std() == 0 or y.std() == 0:
        return np.nan, np.nan, np.nan, np.nan

    zx = (x - x.mean()) / x.std(ddof=1)
    zy = (y - y.mean()) / y.std(ddof=1)
    u = zx * zy
    ic = u.mean()

    dev = u - ic
    s = float(dev @ dev) / n
    lags = min(lags, n - 1)
    for j in range(1, lags + 1):
        g = float(dev[j:] @ dev[:-j]) / n
        s += 2.0 * (1.0 - j / (lags + 1.0)) * g      # noyau de Bartlett

    s = max(s, 0.0)
    se = np.sqrt(s / n)
    return ic, se, ic - 1.96 * se, ic + 1.96 * se


def two_sided_p(ic: float, se: float) -> float:
    if not np.isfinite(ic) or not np.isfinite(se) or se == 0:
        return np.nan
    from math import erfc, sqrt
    return erfc(abs(ic / se) / sqrt(2.0))


def holm(pvals: list[float], alpha: float = ALPHA) -> list[bool]:
    """Holm-Bonferroni. Renvoie la liste des rejets, dans l'ordre d'entree."""
    m = len(pvals)
    order = sorted(range(m), key=lambda i: (np.inf if np.isnan(pvals[i]) else pvals[i]))
    out = [False] * m
    for rank, i in enumerate(order):
        p = pvals[i]
        if np.isnan(p) or p > alpha / (m - rank):
            break
        out[i] = True
    return out


def split_train_oos(df: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame]:
    days = sorted(df["date"].unique())
    cut = int(len(days) * TRAIN_FRAC)
    train_days = days[:cut]
    oos_days = days[cut + PURGE_DAYS:]
    # reset_index obligatoire : forward_returns ecrit par position, et les
    # labels laisses par un filtre booleen ne sont pas des positions.
    return (
        df[df["date"].isin(train_days)].reset_index(drop=True),
        df[df["date"].isin(oos_days)].reset_index(drop=True),
    )


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    grids = Path(sys.argv[1])
    out_dir = Path(sys.argv[2]) if len(sys.argv) > 2 else grids.parent / "Output"
    out_dir.mkdir(parents=True, exist_ok=True)

    df = load_grids(grids)
    train, oos = split_train_oos(df)

    print(f"Points de grille : {len(df):,}")
    print(f"Jours            : {df['date'].nunique()}")
    print(f"TRAIN            : {train['date'].nunique()} j, {len(train):,} pts")
    print(f"OOS              : {oos['date'].nunique()} j, {len(oos):,} pts")
    print(f"Purge            : {PURGE_DAYS} j\n")

    # z-score calibre sur TRAIN seul (v4 Â§3).
    stats = {c: (train[c].mean(), train[c].std(ddof=1)) for c in INDICATORS}

    rows = []
    for h in HORIZONS:
        for part_name, part in (("TRAIN", train), ("OOS", oos)):
            fwd = forward_returns(part, h)
            sd_ret_ticks = np.nanstd(fwd) * part["mid"].mean() / TICK_ES

            for ind in INDICATORS:
                mu, sd = stats[ind]
                z = (part[ind].to_numpy(np.float64) - mu) / (sd if sd else 1.0)

                ic, se, lo, hi = newey_west_ic(z, fwd, lags=h)
                move = abs(ic) * K_ALL_SIGNALS * sd_ret_ticks if np.isfinite(ic) else np.nan

                rows.append({
                    "horizon_s": h,
                    "sample": part_name,
                    "indicator": ind,
                    "ic": ic,
                    "se_nw": se,
                    "ci95_lo": lo,
                    "ci95_hi": hi,
                    "p_value": two_sided_p(ic, se),
                    "sd_ret_ticks_es": sd_ret_ticks,
                    "move_ticks_es": move,
                    "move_ticks_mes": move * 10.0,   # 1 tick ES = 10 ticks MES en valeur
                    "gate_es_pass": bool(move >= GATE_ES_TICKS) if np.isfinite(move) else False,
                })

    res = pd.DataFrame(rows)

    # Holm sur les 24 tests TRAIN (v4 Â§5).
    # La colonne est creee explicitement en bool AVANT l'affectation : creer
    # une colonne et n'en remplir qu'un sous-ensemble via .loc avec une liste
    # Python brute est un chemin pandas fragile qui tente d'ecrire la liste
    # entiere comme valeur unique.
    res["holm_reject"] = False
    tr = (res["sample"] == "TRAIN").to_numpy()
    res.loc[tr, "holm_reject"] = np.asarray(
        holm(res.loc[tr, "p_value"].tolist()), dtype=bool
    )

    path = out_dir / "qde022_ic_curve.csv"
    res.to_csv(path, index=False)

    pd.set_option("display.width", 200)
    print(res[res["sample"] == "TRAIN"][
        ["horizon_s", "indicator", "ic", "ci95_lo", "ci95_hi",
         "p_value", "holm_reject", "move_ticks_es"]
    ].to_string(index=False, float_format=lambda v: f"{v:9.5f}"))

    print(f"\nGate ES : {GATE_ES_TICKS} tick | Gate MES : {GATE_MES_TICKS} ticks")
    print(f"Resultats -> {path}")
    print("\nAucun backtest, aucun P&L, aucune calibration n'a ete produit.")


if __name__ == "__main__":
    main()
