# COLUMN-01 — Ancrage et recouvrement d'une barre HA16

## 1. Énoncé

| Donnée | Valeur |
|---|---|
| Béton | C25/30 → f_ck = 25 MPa |
| Acier | B500 → f_yk = 500 MPa |
| Barre | HA16, droite, verticale |
| Conditions d'adhérence | Bonnes (η₁ = 1,0) |
| Coefficients partiels | γ_c = 1,5 ; γ_s = 1,15 (valeurs recommandées) |
| Hypothèse | Barre pleinement sollicitée : σ_sd = f_yd |
| Recouvrement | Plus de 50 % des barres au même endroit → α₆ = 1,5 |

## 2. Calcul manuel

**Résistance en traction du béton** — EN 1992-1-1, tableau 3.1

```
f_ctm      = 0,30 × f_ck^(2/3) = 0,30 × 25^(2/3) = 0,30 × 8,5499 = 2,565 MPa
f_ctk,0,05 = 0,7 × f_ctm       = 0,7 × 2,565     = 1,7955 MPa
f_ctd      = α_ct × f_ctk,0,05 / γ_c = 1,0 × 1,7955 / 1,5 = 1,197 MPa
```

**Contrainte ultime d'adhérence** — art. 8.4.2 (2)

```
η₁ = 1,0   (bonnes conditions d'adhérence)
η₂ = 1,0   (φ = 16 mm ≤ 32 mm)
f_bd = 2,25 × η₁ × η₂ × f_ctd = 2,25 × 1,197 = 2,693 MPa
```

**Longueur d'ancrage de référence** — art. 8.4.3

```
σ_sd    = f_yk / γ_s = 500 / 1,15 = 434,78 MPa
l_b,rqd = (φ/4) × (σ_sd / f_bd) = (16/4) × (434,78 / 2,693) = 4 × 161,4 = 645,6 mm
```

**Longueur de recouvrement** — art. 8.7.3, tableau 8.3

```
l₀      = α₆ × l_b,rqd = 1,5 × 645,6 = 968,4 mm
l₀,min  = max(0,3 × α₆ × l_b,rqd ; 15 φ ; 200 mm)
        = max(0,3 × 968,4 ; 240 ; 200) = max(290,5 ; 240 ; 200) = 290,5 mm
l₀      = max(968,4 ; 290,5) = 968,4 mm ≈ 60,5 φ
```

## 3. Résultat du moteur

`Anchorage.Compute(16.0, ConcreteProperties(C25, B500), RecommendedAnnex())`

| Grandeur | Moteur |
|---|---|
| f_bd | 2,693 MPa |
| l_b,rqd | 645,6 mm |
| l₀ | 968,4 mm |

Arrondi appliqué par le module de dimensionnement : **1 000 mm** (multiple de 50 mm supérieur).

## 4. Comparaison

| Grandeur | Manuel | Moteur | Écart |
|---|---|---|---|
| f_bd | 2,693 MPa | 2,693 MPa | 0,0 % |
| l_b,rqd | 645,6 mm | 645,6 mm | 0,0 % |
| l₀ | 968,4 mm | 968,4 mm | 0,0 % |

## 5. Conclusion

✅ **Validé.** Le calcul est reproduit exactement.

Test automatisé associé : `tests/DanCI.Structural.Tests/EC2/AnchorageTests.cs`,
`Cas_De_Reference_C25_B500_HA16`.

**Limite connue** : les coefficients α₁ à α₅ de l'article 8.4.4 (forme de la barre, enrobage,
armatures transversales, confinement) sont pris égaux à 1,0. Le résultat est donc
**sécuritaire** ; leur prise en compte fera l'objet d'une évolution ultérieure.
