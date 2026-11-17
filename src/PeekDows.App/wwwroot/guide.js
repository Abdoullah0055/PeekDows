"use strict";

const guideLessons = [
  {
    title: "Ranger et annuler",
    flow: [
      ["spread", "Avant"],
      ["halves", "Aperçu", "Entrée / Échap"],
      ["spread", "Annuler", "Ctrl+Win+Z"],
    ],
    details: ["Arrange Now · rangement immédiat ({arrange})", "Entrée applique · Échap ferme", "Undo · trois rangements"],
    practice: "Essaie avec deux fenêtres.",
    links: [["hotkeys", "Raccourcis"]],
  },
  {
    title: "Manuel, auto ou pause",
    compare: [
      ["key", "Manuel", "{arrange}"],
      ["toggle", "Auto arrange", "À l’ouverture ou fermeture"],
      ["pause", "Pause", "5 min · 15 min · 1 h"],
    ],
    details: [
      "Pause ou reprise · {pause}",
      "Démarrage Windows et rangement au lancement",
      "Animations, notifications et activation",
      "Save applique. Cancel annule le brouillon.",
    ],
    practice: "En présentation, mets en pause.",
    links: [["general", "General"]],
  },
  {
    title: "Dispositions et écrans",
    compare: [
      ["grid", "Grid", "Superposé"],
      ["halves", "Halves", "2 colonnes"],
      ["thirds", "Thirds", "3 colonnes"],
      ["custom", "Custom", "Séparateurs réglables"],
    ],
    details: [
      "Grid · Small, Medium, Large",
      "Edit profiles · mode et règles par écran",
      "Next window in zone · fenêtres empilées",
      "Fenêtre trop grande · zone de travail entière",
      "Fenêtre seule · maximisée en mode standard",
    ],
    practice: "Essaie Halves sur ton portable.",
    links: [["layout", "Layout"]],
  },
  {
    title: "Épingler ou ignorer",
    compare: [
      ["pin", "Épingler", "Case Grid fixe"],
      ["ignore", "Ignorer", "Fenêtre immobile"],
    ],
    details: [
      "Windows → Refresh · fenêtres admissibles",
      "Ignored apps · applications et classes",
      "Règle d’écran prioritaire",
      "Layout · inclure les fenêtres maximisées",
    ],
    practice: "Ignore une app, puis vérifie l’aperçu.",
    links: [["windows", "Windows"]],
  },
  {
    title: "Focus directionnel",
    flow: [
      ["key", "Maintiens", "Ctrl+Win"],
      ["gesture", "Vise", "Vers la fenêtre"],
      ["focus", "Relâche"],
    ],
    details: ["General · activer Directional focus", "Layout · régler le seuil", "Beta · repère et icônes"],
    practice: "Geste trop long ? Baisse le seuil.",
    links: [["layout", "Seuil du geste"]],
  },
  {
    title: "Son du rangement",
    flow: [
      ["sound", "Choisis", "Intégré ou WAV/MP3"],
      ["play", "Écoute"],
      ["clock", "Arrêt auto", "Facultatif"],
    ],
    details: ["Joué après un rangement réussi", "Import · 5 Mo maximum", "Auto-off · 0 h 00 = sans limite"],
    practice: "Écoute un son dans Beta.",
    links: [["beta", "Beta"]],
  },
  {
    title: "Exporter et importer",
    flow: [
      ["export", "Exporter", "JSON"],
      ["review", "Vérifier", "Aperçu"],
      ["import", "Importer", "Sauvegarde auto"],
    ],
    details: [
      "Export · modes, profils, règles et raccourcis",
      "About · version, journaux et dossier des réglages",
      "Menu PeekDows · état, Settings et Exit",
    ],
    practice: "Exporte tes profils depuis le menu PeekDows.",
    links: [["about", "About"]],
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

const guideSymbols = {
  key: "⌨", toggle: "⏻", pause: "Ⅱ", pin: "⌖", ignore: "⊘",
  gesture: "↗", sound: "♫", play: "▶", clock: "◷", export: "⇧", review: "✓", import: "⇩",
};

const guideWindowCounts = { spread: 3, grid: 3, halves: 2, thirds: 3, custom: 3, focus: 2 };

function guideFigure([kind, title, description]) {
  const figure = document.createElement("figure");
  figure.className = "guide-node";
  const graphic = document.createElement("div");
  graphic.className = `guide-graphic guide-graphic-${kind}`;
  graphic.setAttribute("aria-hidden", "true");
  const windowCount = guideWindowCounts[kind] ?? 0;
  if (windowCount) {
    for (let index = 0; index < windowCount; index++) graphic.append(document.createElement("span"));
  } else {
    graphic.textContent = guideSymbols[kind];
  }
  const caption = document.createElement("figcaption");
  const label = document.createElement("strong");
  label.textContent = title;
  caption.append(label);
  if (description) {
    const hint = document.createElement("span");
    hint.textContent = guideShortcuts(description);
    caption.append(hint);
  }
  figure.append(graphic, caption);
  return figure;
}

function guideShortcuts(text) {
  const shortcuts = state.snapshot?.hotkeys;
  return text.replace("{arrange}", shortcuts?.arrangeNow ?? "Ctrl+Alt+Espace")
    .replace("{pause}", shortcuts?.pauseResume ?? "Ctrl+Alt+P");
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
  const diagram = $("guideDiagram");
  diagram.className = lesson.flow ? "guide-diagram guide-flow" : "guide-diagram";
  diagram.replaceChildren(...(lesson.flow ?? lesson.compare).map(guideFigure));
  $("guideMore").open = false;
  $("guideDetails").replaceChildren(...lesson.details.map(text => {
    const item = document.createElement("li");
    item.textContent = guideShortcuts(text);
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
