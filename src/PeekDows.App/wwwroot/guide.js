"use strict";

const guideLessons = [
  {
    title: "Ranger, prévisualiser et annuler",
    details: [
      "Ouvrez le menu de l’icône PeekDows près de l’horloge. « Arrange Now » range les fenêtres admissibles sur leur écran. Votre raccourci enregistré est {arrange}.",
      "« Preview arrange… » montre où iront les fenêtres et lesquelles resteront en place. Entrée applique l’aperçu, Échap le ferme.",
      "« Undo arrange » ou Ctrl+Win+Z annule le dernier rangement. Vous pouvez remonter jusqu’à trois rangements.",
    ],
    practice: "Ouvrez deux fenêtres. Lancez l’aperçu, appliquez-le, puis essayez Undo.",
    links: [["hotkeys", "Voir les raccourcis"]],
  },
  {
    title: "Choisir quand ranger",
    details: [
      "Dans General, activez « Auto arrange » pour réagir aux ouvertures et fermetures de fenêtres. Laissez-le désactivé pour garder la main sur chaque rangement.",
      "« Pause for » suspend le rangement pendant 5 min, 15 min, 1 h ou jusqu’à la reprise manuelle. Votre raccourci enregistré pour mettre en pause ou reprendre est {pause}.",
      "« Start with Windows » lance PeekDows à la connexion. « Arrange on startup » range au lancement. General permet aussi de contrôler les animations, les notifications et l’activation de PeekDows.",
      "Utilisez « Save » dans Settings pour appliquer vos modifications ou « Cancel » pour abandonner le brouillon et fermer la fenêtre.",
    ],
    practice: "Si vous faites des présentations, essayez le mode manuel et une pause de 15 minutes.",
    links: [["general", "Ouvrir General"], ["hotkeys", "Personnaliser les raccourcis"]],
  },
  {
    title: "Adapter les dispositions à vos écrans",
    details: [
      "Grid superpose les fenêtres en laissant leurs bords visibles. Small, Medium et Large ajustent la taille des fenêtres de cette disposition.",
      "Halves partage l’écran en deux colonnes. Thirds le partage en trois. Si plusieurs fenêtres partagent une zone, « Next window in zone » les fait défiler. L’aperçu indique leur nombre.",
      "« Edit profiles » permet de choisir un mode et des règles par application pour un écran. Les écrans sans profil utilisent les réglages par défaut.",
      "Custom offre un partage comme 60/40 et une seconde séparation horizontale à droite. Une fenêtre qui ne tient pas dans sa zone peut utiliser toute la zone de travail. Les dispositions standard maximisent normalement une fenêtre admissible seule sur son écran.",
    ],
    practice: "Essayez Halves sur un portable ou Thirds sur un écran large. Pour donner plus d’espace à un éditeur, essayez Custom à 65/35.",
    links: [["layout", "Ouvrir Layout"]],
  },
  {
    title: "Épingler ou exclure des applications",
    details: [
      "Dans Windows, « Refresh » affiche les fenêtres ouvertes, leur processus et leur admissibilité. Vous pouvez épingler une application à une case Grid ou l’exclure.",
      "Dans Ignored apps, recherchez une application ouverte pour l’exclure partout. La section avancée accepte les classes de fenêtres, une par ligne. PeekDows gère les exclusions système intégrées.",
      "Une règle définie dans un profil d’écran prend la priorité sur la règle par défaut pour la même application.",
      "Dans Layout, activez « Reposition maximized windows » si vous voulez inclure les fenêtres que vous avez maximisées.",
    ],
    practice: "Excluez une application que vous gardez toujours au même endroit. Sauvegardez et vérifiez dans l’aperçu qu’elle reste en place.",
    links: [["windows", "Voir les fenêtres et les règles"], ["ignored", "Gérer les exclusions"], ["layout", "Régler les fenêtres maximisées"]],
  },
  {
    title: "Changer de fenêtre avec la souris",
    details: [
      "Activez « Directional focus » dans General. Maintenez Ctrl+Win, déplacez la souris vers une zone occupée, puis relâchez pour activer la fenêtre.",
      "Dans Layout, une valeur plus basse pour « Directional focus threshold » demande un mouvement plus court.",
      "Dans Beta, vous pouvez afficher ou masquer le repère directionnel et choisir si les icônes des applications apparaissent dans les zones.",
    ],
    practice: "Rangez deux fenêtres et essayez le geste dans les deux directions. Ajustez ensuite le seuil selon l’amplitude de votre mouvement.",
    links: [["general", "Activer le focus directionnel"], ["layout", "Ajuster le seuil"], ["beta", "Régler le repère"]],
  },
  {
    title: "Personnaliser le son",
    details: [
      "Dans Beta, « Arrange sound » joue un son après un rangement réussi. Cliquez sur un son intégré pour l’écouter et le sélectionner.",
      "« Ajouter un son » importe un WAV ou MP3 de 5 Mo maximum. Écoutez-le depuis « Mes sons » et sauvegardez votre choix.",
      "« Auto-off » coupe le son après un délai. Laissez 0 h 00 pour le garder actif.",
    ],
    practice: "Essayez un son discret. Sur un poste partagé, désactivez le son ou programmez son arrêt.",
    links: [["beta", "Ouvrir les réglages du son"]],
  },
  {
    title: "Transférer votre configuration et dépanner",
    details: [
      "Dans le menu de l’icône, « Layout profiles → Export layout settings… » enregistre vos dispositions, vos profils, vos règles et vos raccourcis dans un fichier JSON.",
      "« Import layout settings… » affiche les changements à confirmer. PeekDows sauvegarde les réglages remplacés avant d’appliquer l’import.",
      "Dans About, retrouvez la version, le journal et les dossiers des journaux et des réglages. « Windows → Refresh » aide à comprendre pourquoi une fenêtre reste en place.",
      "Le menu de l’icône affiche l’état du rangement et permet d’ouvrir Settings ou de quitter PeekDows avec « Exit ».",
    ],
    practice: "Exportez votre configuration lorsque vos profils sont prêts. Après un import sur un autre poste, vérifiez leur association aux écrans.",
    links: [["layout", "Revoir les profils"], ["about", "Voir la version et les journaux"]],
  },
];

let guideStep = null;

function guideButton(label, onClick) {
  const button = document.createElement("button");
  button.className = "btn";
  button.textContent = label;
  button.addEventListener("click", onClick);
  return button;
}

function guideNavigate(page) {
  const tab = document.querySelector(`.navitem[data-page="${page}"]`);
  tab?.click();
  document.querySelector(".content").scrollTop = 0;
  tab?.focus();
}

function showGuideContents() {
  $("guideIntro").hidden = false;
  $("guideResume").hidden = guideStep === null;
  $("guideLesson").hidden = true;
  $("guideDone").hidden = true;
  $("guideStart").focus();
}

function showGuideLesson(index) {
  const lesson = guideLessons[index];
  if (!lesson) return;
  guideStep = index;
  $("guideIntro").hidden = true;
  $("guideDone").hidden = true;
  $("guideLesson").hidden = false;
  $("guideProgress").textContent = `Chapitre ${index + 1} sur ${guideLessons.length}`;
  $("guideTitle").textContent = lesson.title;
  const shortcuts = state.snapshot?.hotkeys;
  $("guideDetails").replaceChildren(...lesson.details.map(text => {
    const item = document.createElement("li");
    item.textContent = text.replace("{arrange}", shortcuts?.arrangeNow ?? "Ctrl+Alt+Espace")
      .replace("{pause}", shortcuts?.pauseResume ?? "Ctrl+Alt+P");
    return item;
  }));
  $("guidePractice").textContent = lesson.practice;
  $("guideLinks").replaceChildren(...lesson.links.map(([page, label]) =>
    guideButton(label, () => guideNavigate(page))));
  $("guidePrevious").disabled = index === 0;
  $("guideNext").textContent = index === guideLessons.length - 1 ? "Terminer" : "Suivant";
  $("guideTitle").focus();
}

$("openGuide").addEventListener("click", () => {
  guideNavigate("guide");
  showGuideContents();
});
$("guideStart").addEventListener("click", () => showGuideLesson(0));
$("guideResume").addEventListener("click", () => showGuideLesson(guideStep));
$("guideContents").addEventListener("click", showGuideContents);
$("guideReview").addEventListener("click", showGuideContents);
$("guidePrevious").addEventListener("click", () => showGuideLesson(guideStep - 1));
$("guideNext").addEventListener("click", () => {
  if (guideStep === null) return;
  if (guideStep < guideLessons.length - 1) {
    showGuideLesson(guideStep + 1);
    return;
  }
  guideStep = null;
  $("guideLesson").hidden = true;
  $("guideDone").hidden = false;
  $("guideDoneTitle").focus();
});

$("guideChapters").replaceChildren(...guideLessons.map((lesson, index) => {
  const item = document.createElement("li");
  item.append(guideButton(lesson.title, () => showGuideLesson(index)));
  return item;
}));
