# Plan de correction des réglages

**Objectif** · Rendre les positions d’épinglage visibles, permettre de revenir sur une exclusion ou un brouillon et capturer des raccourcis complets.

- Dans `index.html`, `app.css` et `app.js`, afficher les huit positions Grid à côté de chaque fenêtre. Marquer la position sélectionnée et les applications déjà épinglées. Conserver le partage des cases possible sur des écrans différents.
- Vérifier que le moteur place une application dans une case sélectionnée même avec moins de huit fenêtres. Reprendre les coordonnées du moteur sur chaque écran.
- Dans `app.js`, afficher les états Ignorer et Ignoré. Réutiliser le dialogue de confirmation depuis Windows et Ignored apps pour retirer une exclusion avant sauvegarde.
- Faire de Cancel une remise à zéro du brouillon, sur la page courante, à partir du dernier état reçu. Conserver les imports audio déjà copiés et les changements du menu Windows.
- Garder la capture clavier ouverte pendant la saisie des modificateurs. Accepter les touches prises en charge par le moteur. Échap ou le départ du champ doit interrompre la capture.
- Couvrir les quatre parcours avec les tests JavaScript. Relancer aussi la suite .NET et copier les ressources validées vers Debug sans ouvrir l’application.
