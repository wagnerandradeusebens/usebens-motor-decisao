/** pt-BR formula functions, mirroring the backend FunctionLibrary. Used by the
 *  autocomplete and the "Funções" reference section. */
export interface FunctionDoc {
  name: string;
  signature: string;
  description: string;
}

export const FUNCTIONS: FunctionDoc[] = [
  { name: 'SE', signature: 'SE(condição; então; senão)', description: 'Retorna um valor se verdadeiro, outro se falso.' },
  { name: 'E', signature: 'E(cond1; cond2; …)', description: 'Verdadeiro se todas as condições forem verdadeiras.' },
  { name: 'OU', signature: 'OU(cond1; cond2; …)', description: 'Verdadeiro se alguma condição for verdadeira.' },
  { name: 'NAO', signature: 'NAO(condição)', description: 'Inverte um valor booleano.' },
  { name: 'SEERRO', signature: 'SEERRO(valor; alternativa)', description: 'Usa a alternativa quando o valor resulta em erro.' },
  { name: 'SOMA', signature: 'SOMA(a; b; …)', description: 'Soma os valores.' },
  { name: 'ARRED', signature: 'ARRED(número; casas)', description: 'Arredonda para o número de casas decimais.' },
  { name: 'ARREDONDAR.PARA.CIMA', signature: 'ARREDONDAR.PARA.CIMA(número; casas)', description: 'Arredonda para cima.' },
  { name: 'ARREDONDAR.PARA.BAIXO', signature: 'ARREDONDAR.PARA.BAIXO(número; casas)', description: 'Arredonda para baixo.' },
  { name: 'ABS', signature: 'ABS(número)', description: 'Valor absoluto.' },
  { name: 'MINIMO', signature: 'MINIMO(a; b; …)', description: 'Menor valor.' },
  { name: 'MAXIMO', signature: 'MAXIMO(a; b; …)', description: 'Maior valor.' },
  { name: 'RESTO', signature: 'RESTO(número; divisor)', description: 'Resto da divisão.' },
  { name: 'RAIZ', signature: 'RAIZ(número)', description: 'Raiz quadrada.' },
  { name: 'RAIZCUBICA', signature: 'RAIZCUBICA(número)', description: 'Raiz cúbica (aceita negativos).' },
  { name: 'TRUNCAR', signature: 'TRUNCAR(número; casas)  ou  TRUNCAR(texto; n)', description: 'Trunca um número (sem arredondar) ou os primeiros n caracteres de um texto.' },
  { name: 'CONCATENAR', signature: 'CONCATENAR(texto1; texto2; …)', description: 'Junta textos.' },
  { name: 'NUM.CARACT', signature: 'NUM.CARACT(texto)', description: 'Número de caracteres.' },
  { name: 'MAIUSCULA', signature: 'MAIUSCULA(texto)', description: 'Converte para maiúsculas.' },
  { name: 'MINUSCULA', signature: 'MINUSCULA(texto)', description: 'Converte para minúsculas.' },
  { name: 'ARRUMAR', signature: 'ARRUMAR(texto)', description: 'Remove espaços das extremidades.' },
  { name: 'ESQUERDA', signature: 'ESQUERDA(texto; n)', description: 'Primeiros n caracteres.' },
  { name: 'DIREITA', signature: 'DIREITA(texto; n)', description: 'Últimos n caracteres.' },
  { name: 'HOJE', signature: 'HOJE()', description: 'Data atual.' },
  { name: 'ANO', signature: 'ANO(data)', description: 'Ano de uma data.' },
  { name: 'MES', signature: 'MES(data)', description: 'Mês de uma data.' },
  { name: 'DIA', signature: 'DIA(data)', description: 'Dia de uma data.' },
  { name: 'DATADIF', signature: 'DATADIF(início; fim; "Y"|"M"|"D")', description: 'Diferença entre datas (anos/meses/dias).' },
  { name: 'EHNUM', signature: 'EHNUM(valor)', description: 'Verdadeiro se for número.' },
  { name: 'EHBRANCO', signature: 'EHBRANCO(valor)', description: 'Verdadeiro se estiver em branco.' },
];

export const FUNCTION_NAMES = FUNCTIONS.map((f) => f.name);
