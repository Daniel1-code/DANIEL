# Protocole de validation du métré — DanCI Métré & BOQ (Étape 8)

**Objectif du cahier des charges**

1. Sur une maquette test, comparer le métré de l'extension à un **métré manuel** : **écart
   inférieur à 2 % sur le béton et sur l'acier**, chaque écart étant expliqué.
2. Le **même fichier**, analysé dans **Revit 2026 puis dans Revit 2027**, doit donner
   **exactement les mêmes quantités**. Sinon, il faut en identifier la cause.

Ce protocole décrit comment faire ces deux comparaisons avec les outils de l'onglet
**Validation** de la fenêtre « Métré & BOQ ». **Les résultats ne sont valables qu'une fois
obtenus sur votre maquette** : aucun chiffre de validation n'est fourni d'avance.

---

## 1. Critères

| Critère | Mesure | Seuil |
|---|---|---|
| Béton (`VolumeBeton`) | écart sur le **total** des éléments mesurés des deux côtés | ≤ 2 % |
| Acier, armatures (`Armature`) | idem | ≤ 2 % |
| Acier, charpente (`AcierCharpente`) | idem | ≤ 2 % |
| Coffrage, maçonnerie… | idem | 5 %, à titre indicatif |
| Écarts élément par élément | **chacun expliqué**, même si le total est conforme | — |
| Reproductibilité 2026 / 2027 | empreinte du métré (SHA-256, 6 décimales) | identique |

Écart % = (modèle − manuel) ÷ manuel : **la référence est le métré manuel**.

**Pourquoi le total, et pas chaque élément ?** Au droit d'une jointure (poteau / poutre /
dalle), Revit attribue le volume commun à **un seul** des éléments, selon l'ordre de jointure. Un
métreur peut l'attribuer à l'autre. Les deux métrés sont alors justes au total, mais différents
élément par élément. Le rapport donne les deux niveaux : le total juge, le détail explique.

---

## 2. Préparation

1. **Travailler sur une copie** de la maquette. L'ouverture dans Revit 2027 met le fichier à
   niveau, et l'écriture des codes modifie le modèle.
2. **Ratios d'armatures** : pour juger l'acier, modéliser les armatures des éléments de
   l'échantillon, ou exclure de la comparaison les éléments estimés par ratio. Un ratio n'est
   pas une mesure.
3. **Configuration** : utiliser le même dossier `config` sur les deux postes (2026 et 2027).
   Comparer les fichiers en cas de doute.
4. **Portée** : modèle entier, **même phase**, mêmes liens dans les deux analyses.
5. **Audit** : corriger d'abord les erreurs de l'onglet Audit, surtout les chevauchements non
   joints (rubrique 6) et le béton sans classe (rubrique 2). Valider un métré faussé par le
   modèle n'apprend rien sur l'extension.

### Échantillon minimal recommandé

| Ouvrage | Nombre | Ce qu'il éprouve |
|---|---|---|
| Poteaux béton | 3 (dont 1 d'angle) | volume, jointures poteau / poutre, coffrage avec abouts de poutres |
| Poutres béton | 3 (dont 1 continue) | volume entre nus, coffrage des joues et du fond |
| Dalle | 1 (avec une trémie) | volume, sous-face coffrée, dessus non coffré |
| Semelles | 2 | volume, sous-face non coffrée, armatures des fondations |
| Voile béton | 1 | volume, coffrage deux faces |
| Murs maçonnés | 2 (1 sans ouverture, 1 avec une fenêtre < 0,5 m² et une porte) | surface à l'axe, règle de déduction |
| Armatures modélisées | les éléments ferraillés de l'échantillon | longueur développée × kg/m |
| Profilés métalliques | 3 | longueur de coupe × kg/m |

---

## 3. Conventions du métré manuel

Le métré manuel doit suivre **les mêmes conventions** que l'extension, faute de quoi les écarts
mesurent la différence de convention, et non une erreur. Les conventions de l'extension sont
détaillées dans la feuille **Hypothèses** de l'export Excel.

| Ouvrage | Convention de l'extension (à appliquer à la main) |
|---|---|
| Béton | Volume **net** en place de chaque matériau béton. Aux intersections : voir §1, comparer les totaux. Pas de pertes (elles relèvent des coefficients). |
| Armatures | Longueur **développée** de chaque barre, crochets et façonnage compris (comme dans Revit), × masse linéique HA du diamètre (HA6 0,222 … HA40 9,865 kg/m). **Sans** les recouvrements non modélisés (ils relèvent du coefficient de chutes). |
| Charpente | Longueur **de coupe** × masse linéique du catalogue (ou poids nominal du type). Assemblages exclus de la comparaison (estimés). |
| Coffrage | Faces latérales et sous-faces, **moins** les surfaces en contact avec un autre élément. **Sans** les dessus de pente < 30°, les sous-faces de fondations, ni les sous-faces de dallages sur terre-plein. |
| Maçonnerie | Surface **à l'axe** du mur (moyenne des deux faces). Ouvertures de plus de 0,5 m² déduites, d'après la **baie brute**. |

**Le gabarit ne montre pas les quantités du modèle, volontairement** : un métré fait à côté du
résultat à vérifier n'est plus indépendant.

---

## 4. Procédure

### 4.1 Comparaison au métré manuel

1. Fenêtre **Métré & BOQ** → **Analyser** (portée et phase de la validation).
2. Onglet **Validation** → **Générer le gabarit de métré manuel**. Le fichier
   `…_validation_gabarit.xlsx` est créé à côté du projet.
3. Le métreur remplit la colonne jaune **Quantité manuelle** des éléments de l'échantillon, et
   peut noter sa méthode dans la colonne Commentaire. Les autres lignes restent vides : elles
   sont ignorées.
4. Facultatif : la feuille « articles » reçoit les totaux nets par article.
5. **Comparer au gabarit rempli…** → `…_validation_rapport.xlsx` :
   - **Synthèse** : par nature, totaux modèle / manuel, écart, verdict, puis la conclusion
     « OBJECTIF ATTEINT » ou « NON ATTEINT » ;
   - **Par élément** : écart de chaque élément ; les lignes hors tolérance sont en rouge, avec
     des **pistes d'explication** tirées de la méthode de mesure ;
   - **Par article** : quantités nettes du BOQ contre totaux manuels.
6. Pour chaque ligne rouge, établir la cause (voir §5) et la consigner dans la fiche (§7).

### 4.2 Reproductibilité Revit 2026 / 2027

1. Dans **Revit 2026** : ouvrir la copie, analyser, **Exporter Excel / PDF**. Garder le fichier
   `…_metre_brut.csv`. Noter l'**empreinte du métré** (onglet Validation, page de garde de
   l'Excel, pied du PDF).
2. Dans **Revit 2027** : ouvrir **une autre copie** du même fichier (mise à niveau), refaire
   exactement la même analyse, exporter, noter l'empreinte.
3. **Empreintes égales** : les quantités sont identiques à 6 décimales près et les statuts sont
   les mêmes. Critère rempli. **La comparaison des deux CSV fait foi.** Le CSV garde
   9 décimales ; dans un cas limite d'arrondi, l'empreinte affichée dans la fenêtre peut différer
   de celle recalculée depuis le CSV, alors que la comparaison CSV contre CSV reste cohérente.
4. **Empreintes différentes** : onglet Validation → **Comparer deux métrés bruts (CSV)…**, choisir
   les deux fichiers. Le rapport `DanCI_reproductibilite_….xlsx` liste chaque quantité qui
   diffère (valeur, absence, statut).

---

## 5. Interprétation des écarts

| Constat | Causes probables | Vérification |
|---|---|---|
| Béton : un élément trop fort, son voisin trop faible, total juste | attribution du volume à la jointure (§1) | comparer la somme des deux ; changer l'ordre de jointure dans Revit inverse l'écart |
| Béton : modèle plus fort au total | chevauchement non joint (compté deux fois) ; surépaisseur modélisée | audit, rubrique 6 ; « Joindre la géométrie » |
| Béton : modèle plus faible | réservation ou vide modélisé ; couche non béton dans l'élément ; matériau non reconnu comme béton | détail du métré (matériau, `ReconnaissanceBeton`) |
| Armatures : modèle plus fort | crochets et façonnage comptés en longueur développée par Revit, pas par le métreur | comparer au carnet de ferraillage en longueurs développées |
| Armatures : modèle plus faible | recouvrements ou attentes non modélisés ; élément partiellement ferraillé | audit, rubrique 3 (« ferraillage incomplet ») |
| Charpente | longueur de coupe (après coupes et ajustements) contre longueur entre axes ; poids nominal du type contre catalogue | colonne Méthode : source de la masse linéique |
| Coffrage | contacts déduits, dessus et sous-faces exclus : convention différente | paramètres `FacesLateralesM2`, `ContactsDeduitsM2`… |
| Maçonnerie | face de mesure (axe contre face) ; règle de déduction ; baie brute contre baie nominale | paramètres `SurfaceFacePerceeM2`, `OuverturesRajouteesM2` |
| Élément « absent du modèle » | exclu (phase, portée, `DanCI_BOQ_Exclure`, option de conception) ; ElementId changé (élément recréé) | onglets Exclus et Audit |

**Reproductibilité 2026 / 2027 : causes possibles d'une différence**

- géométrie recalculée différemment par la nouvelle version (jointures, opérations booléennes),
  en particulier pour le **coffrage**, qui dépend des intersections ;
- mise à niveau du fichier : familles ou matériaux modifiés à l'ouverture ;
- configuration DanCI différente sur les deux postes ;
- portée, phase ou liens différents ;
- liens chargés dans une version et pas dans l'autre.

Toute différence doit être expliquée, même minime. Si elle vient de Revit lui-même (géométrie
recalculée), elle est documentée dans la fiche comme une limite, pas masquée.

---

## 6. Ce que la validation ne couvre pas

- Les **quantités estimées** (ratios d'armatures, assemblages, volume × 7850) : ce sont des
  hypothèses, à valider par l'ingénieur, pas par comparaison.
- Les **prix** : la validation porte sur les quantités.
- Les conventions de métré propres à un marché (CCTP) qui différeraient de celles du §3 : il faut
  alors adapter `extraction.json`, puis recommencer.

---

## 7. Fiche de résultats (à remplir)

Copier ce tableau dans `docs/validation/VALID-BOQ-01.md` et le compléter.

| Rubrique | Valeur |
|---|---|
| Maquette | |
| Date | |
| Versions | DanCI Métré & BOQ … / moteur … ; Revit 2026.x build … ; Revit 2027.x build … |
| Portée / phase / liens | |
| Métreur (manuel) | |
| Empreinte Revit 2026 | |
| Empreinte Revit 2027 | |
| Reproductibilité | identique / N différences (causes : …) |
| Béton : total modèle / manuel / écart | |
| Armatures : total modèle / manuel / écart | |
| Charpente : total modèle / manuel / écart | |
| Verdict | OBJECTIF ATTEINT / NON ATTEINT |

| Élément ou article | Écart | Cause établie | Action (corriger le modèle, la configuration ou l'extension ; ou accepter et documenter) |
|---|---|---|---|
| | | | |
