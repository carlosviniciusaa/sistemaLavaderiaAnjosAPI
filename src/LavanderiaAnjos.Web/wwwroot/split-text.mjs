// Entrada escalonada inspirada no SplitText do React Bits, sem dependências.
// Use apenas em textos estáticos, sem elementos filhos ou controles interativos.
const initialized = new WeakSet();

export function splitText(element, { splitType = "chars" } = {}) {
  if (!element || initialized.has(element) || element.children.length ||
      !element.textContent.trim() || !["chars", "words"].includes(splitType)) return;

  const view = element.ownerDocument.defaultView;
  if (view.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;

  const documentRef = element.ownerDocument;
  const originalNodes = [...element.childNodes];
  const label = documentRef.createElement("span");
  label.className = "split-text-label";
  label.append(...originalNodes);

  const visual = documentRef.createElement("span");
  visual.setAttribute("aria-hidden", "true");
  const segmenter = typeof Intl.Segmenter === "function"
    ? new Intl.Segmenter(documentRef.documentElement.lang || "pt-BR", { granularity: "grapheme" })
    : null;
  let index = 0;

  for (const token of label.textContent.split(/(\s+)/u)) {
    if (!token) continue;
    if (/^\s+$/u.test(token)) {
      visual.append(documentRef.createTextNode(token));
      continue;
    }

    // A palavra inteira controla as quebras; os caracteres só cuidam da animação.
    const word = documentRef.createElement("span");
    word.className = "split-text-word";
    const pieces = splitType === "words"
      ? [token]
      : segmenter ? [...segmenter.segment(token)].map(part => part.segment) : Array.from(token);

    for (const piece of pieces) {
      const unit = documentRef.createElement("span");
      unit.className = "split-text-unit";
      unit.textContent = piece;
      unit.style.setProperty("--split-text-index", index++);
      word.append(unit);
    }
    visual.append(word);
  }

  element.replaceChildren(label, visual);
  initialized.add(element);

  return () => {
    element.replaceChildren(...originalNodes);
    initialized.delete(element);
  };
}

if (typeof document !== "undefined") {
  for (const element of document.querySelectorAll("[data-split-text]")) {
    splitText(element, { splitType: element.dataset.splitText || "chars" });
  }
}
