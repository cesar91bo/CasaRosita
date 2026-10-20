window.rosita = {
  print: () => window.print(),
  pending: () => localStorage.getItem('casarosita.cobro'),
  remember: value => localStorage.setItem('casarosita.cobro', value),
  clear: () => localStorage.removeItem('casarosita.cobro')
};
