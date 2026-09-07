# COLUMN-06 — Enrobage selon l'EN 1992-1-1 §4.4.1

## 1. Énoncé

Trois cas courants, couvrant les trois termes qui peuvent gouverner l'enrobage.

| Cas | Exposition | Béton | Barre | Contexte |
|---|---|---|---|---|
| A | XC1 | C25/30 | HA16 | poteau intérieur |
| B | XC4 | C30/37 | HA20 | poteau extérieur exposé aux intempéries |
| C | XS3 | C45/55 | HA25 | poteau en zone de marnage |
| D | XC1 | C25/30 | HA40 | grosse barre : l'adhérence gouverne |

Durée d'utilisation 50 ans, sans contrôle de production spécial, Δc_dev = 10 mm.

## 2. Calcul manuel

### 2.1 Classe structurale — tableau 4.3N

Classe de référence **S4** pour 50 ans, puis :

| Cas | Seuil de résistance (4.3N) | f_ck | Modification | Classe |
|---|---|---|---|---|
| A | C30/37 pour XC1 | 25 | seuil non atteint | **S4** |
| B | C40/50 pour XC4 | 30 | seuil non atteint | **S4** |
| C | C45/55 pour XS3 | 45 | seuil atteint → −1 | **S3** |
| D | C30/37 pour XC1 | 25 | seuil non atteint | **S4** |

### 2.2 Enrobage de durabilité — tableau 4.4N

| Cas | Exposition / classe | c_min,dur |
|---|---|---|
| A | XC1 / S4 | 15 mm |
| B | XC4 / S4 | 30 mm |
| C | XS3 / S3 | 40 mm |
| D | XC1 / S4 | 15 mm |

### 2.3 Enrobage minimal et nominal — art. 4.4.1.2 et 4.4.1.3

```
c_min = max(c_min,b ; c_min,dur ; 10 mm)      avec c_min,b = φ
c_nom = c_min + Δc_dev
```

| Cas | c_min,b | c_min,dur | c_min | **c_nom** | Terme gouvernant |
|---|---|---|---|---|---|
| A | 16 | 15 | 16 | **26 mm** | adhérence |
| B | 20 | 30 | 30 | **40 mm** | durabilité |
| C | 25 | 40 | 40 | **50 mm** | durabilité |
| D | 40 | 15 | 40 | **50 mm** | adhérence |

### 2.4 Effet des modificateurs de classe structurale

| Situation (XC1, C25/30) | Modification | Classe | c_min,dur |
|---|---|---|---|
| Référence | — | S4 | 15 mm |
| Contrôle de production assuré | −1 | S3 | 10 mm |
| Durée d'utilisation 100 ans | +2 | S6 | 25 mm |
| 100 ans + C30/37 + contrôle assuré | +2 −1 −1 | S4 | 15 mm |

## 3. Conclusion

✅ **Validé.** Les quatre cas et les modificateurs de classe structurale sont reproduits.

Tests associés : `tests/DanCI.Structural.Tests/EC2/ConcreteCoverTests.cs`.

**Limites connues** :
- les majorations Δc_dur,γ (sécurité additionnelle), Δc_dur,st (acier inoxydable) et
  Δc_dur,add (protection supplémentaire) sont prises **égales à 0**, valeurs recommandées ;
- le critère « élément de type dalle » de la table 4.3N n'est pas appliqué aux poteaux, ce
  qui est correct pour ce module mais devra être activé pour le module Dalle ;
- Δc_dev = 10 mm est la valeur recommandée ; les réductions autorisées en cas de contrôle
  qualité renforcé (art. 4.4.1.3(3)) ne sont pas appliquées ;
- ces valeurs sont des **NDP** : elles seront à reprendre dans la couche Annexe Nationale.
